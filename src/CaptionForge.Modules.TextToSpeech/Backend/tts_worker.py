"""One CPU job per process, releasing model memory on exit."""
import json
import os
import re
import sys
import time
from pathlib import Path

os.environ.setdefault('HF_HUB_OFFLINE', '1')
os.environ.setdefault('TRANSFORMERS_OFFLINE', '1')
os.environ.setdefault('OMP_NUM_THREADS', '4')
os.environ.setdefault('TQDM_DISABLE', '1')

def report(**data):
    print('TTS_EVENT ' + json.dumps(data, ensure_ascii=True), flush=True)

def chunks(text):
    parts, current = [], ''
    for sentence in re.split(r'(?<=[.!?])\s+|\n+', text):
        if len(sentence) > 450:
            raise ValueError('Divide las frases largas con puntos (máximo 450 caracteres por frase).')
        if len(current) + len(sentence) > 250 and current:
            parts.append(current.strip())
            current = ''
        current += ' ' + sentence
    if current.strip():
        parts.append(current.strip())
    return parts

def run(job):
    import numpy as np
    import soundfile as sf
    import torch
    torch.set_num_threads(int(job.get('threads', 4)))
    torch.set_num_interop_threads(1)
    torch.manual_seed(int(job['seed']))
    started = time.perf_counter()
    engine, lang = job['engine'], job['language']
    report(status=f'Cargando {engine} en CPU…')
    if engine == 'Nano':
        from chatterbox.tts_turbo import ChatterboxTurboTTS
        if sf.info(job['reference']).duration <= 5:
            raise ValueError('La referencia debe durar más de cinco segundos.')
        model = ChatterboxTurboTTS.from_local(Path(job['model_directory']), device='cpu', nano=True)
        sr = model.sr
    elif engine == 'Chatterbox Multilingual V3':
        from chatterbox.mtl_tts import ChatterboxMultilingualTTS
        if sf.info(job['reference']).duration <= 5:
            raise ValueError('La referencia debe durar más de cinco segundos.')
        model = ChatterboxMultilingualTTS.from_local(Path(job['model_directory']), device='cpu', t3_model='v3')
        sr = model.sr
    elif engine == 'Kokoro':
        from kokoro import KModel, KPipeline
        folder = Path(job['model_directory'])
        voice = job['voice']
        code = 'e' if lang == 'Español' else ('b' if voice.startswith('b') else 'a')
        model = KModel(repo_id='hexgrad/Kokoro-82M', config=str(folder / 'config.json'), model=str(folder / 'kokoro-v1_0.pth')).to('cpu').eval()
        pipeline = KPipeline(lang_code=code, model=model, repo_id='hexgrad/Kokoro-82M', device='cpu')
        voice_file = folder / 'voices' / (voice + '.pt')
        if not voice_file.is_file():
            raise ValueError('Selecciona una voz de Kokoro para este idioma.')
        sr = 24000
    elif engine == 'Pocket TTS':
        import yaml
        from pocket_tts import TTSModel
        folder = Path(job['model_directory'])
        language = 'spanish' if lang == 'Español' else 'english'
        cfg_file = folder / 'config' / (language + '.yaml')
        if not cfg_file.is_file():
            cfg_file = Path(__file__).resolve().parent / 'config' / (language + '.yaml')
        cfg = yaml.safe_load(cfg_file.read_text(encoding='utf-8'))
        local = folder / 'languages' / language
        cfg['weights_path'] = str(local / 'model.safetensors')
        cfg['weights_path_without_voice_cloning'] = str(local / 'model.safetensors')
        tokenizer = cfg['flow_lm']['lookup_table']
        tokenizer['tokenizer_path'] = str(local / ('tokenizer.json' if tokenizer.get('tokenizer') == 'tokenizers' else 'tokenizer.model'))
        configs = Path(sys.argv[1]).resolve().parent
        config_file = configs / (language + '.yaml')
        config_file.write_text(yaml.safe_dump(cfg, allow_unicode=False), encoding='utf-8')
        try:
            model = TTSModel.load_model(config=config_file)
        finally:
            config_file.unlink(missing_ok=True)
        model.has_voice_cloning = False
        voice_file = local / 'embeddings' / (job['voice'] + '.safetensors')
        if not voice_file.is_file():
            raise ValueError('Selecciona una voz predefinida de Pocket.')
        voice_state = model.get_state_for_audio_prompt(voice_file)
        sr = model.sample_rate
    else:
        raise ValueError('Modelo desconocido.')
    audio, parts = [], chunks(job['text'])
    for i, part in enumerate(parts):
        report(status=f'Generando fragmento {i + 1} de {len(parts)}…', progress=i / len(parts))
        with torch.no_grad():
            if engine == 'Kokoro':
                generated = [item.audio.cpu().numpy() for item in pipeline(part, voice=str(voice_file), speed=float(job['speed'])) if item.audio is not None]
                if not generated:
                    raise ValueError('Kokoro no produjo audio para este fragmento.')
                wav = np.concatenate(generated)
            elif engine == 'Pocket TTS':
                wav = model.generate_audio(voice_state, part).detach().cpu().numpy()
            elif engine == 'Nano':
                wav = model.generate(part, audio_prompt_path=job['reference'] if i == 0 else None).squeeze().cpu().numpy()
            else:
                wav = model.generate(part, language_id='es' if lang == 'Español' else 'en', audio_prompt_path=job['reference'] if i == 0 else None, exaggeration=float(job['exaggeration'])).squeeze().cpu().numpy()
        audio.append(np.asarray(wav, dtype=np.float32).reshape(-1))
        if i < len(parts) - 1:
            audio.append(np.zeros(round(sr * 0.18), dtype=np.float32))
    combined = np.concatenate(audio)
    if not len(combined) or not np.isfinite(combined).all() or float(np.max(np.abs(combined))) == 0:
        raise ValueError('El modelo devolvió audio vacío o inválido.')
    peak = float(np.max(np.abs(combined)))
    if peak > 0.97:
        combined *= 0.97 / peak
    sf.write(job['output'], combined, sr, subtype='PCM_16')
    report(output=job['output'], seconds=len(combined) / sr, elapsed=time.perf_counter() - started, engine=engine, language=lang, sample_rate=sr)

if __name__ == '__main__':
    try:
        run(json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig')))
    except Exception as exc:
        import traceback
        traceback.print_exc()
        report(error=str(exc))
        sys.exit(1)
