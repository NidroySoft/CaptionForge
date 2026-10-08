"""Installs official CPU engines into the module's private venv; downloads pinned model revisions."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import urllib.request
import zipfile

def report(status, progress=0):
    print('TTS_EVENT ' + json.dumps(dict(status=status, progress=progress)), flush=True)

def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'CaptionForge-SpeechModule'}), timeout=120) as response:
        return json.load(response)

def pip(*args):
    subprocess.run([sys.executable, '-m', 'pip', '--disable-pip-version-check', 'install', *args], check=True)

def github_package(repo):
    info = get_json('https://api.github.com/repos/' + repo + '/commits/HEAD')
    return 'https://github.com/' + repo + '/archive/' + info['sha'] + '.zip'

def packages(engine, root):
    report('Instalando dependencias de ' + engine + ' en CPU…')
    pip('--upgrade', 'pip', 'setuptools', 'wheel')
    if engine == 'Pocket':
        pip('torch>=2.6,<3', '--index-url', 'https://download.pytorch.org/whl/cpu')
        pip(github_package('kyutai-labs/pocket-tts'), 'soundfile>=0.12')
    elif engine == 'Kokoro':
        pip('torch==2.6.0', '--index-url', 'https://download.pytorch.org/whl/cpu')
        pip('numpy<2', 'kokoro==0.9.4', 'misaki[en,es]==0.9.4', 'transformers==5.2.0', 'soundfile', 'espeakng-loader==0.2.4')
        pip('https://github.com/explosion/spacy-models/releases/download/en_core_web_sm-3.8.0/en_core_web_sm-3.8.0-py3-none-any.whl')
    else:
        pip('torch==2.6.0', 'torchaudio==2.6.0', '--index-url', 'https://download.pytorch.org/whl/cpu')
        # Upstream uses a Git URL for Perth. Rewrite it to an immutable source ZIP so users do not need Git.
        url = github_package('resemble-ai/chatterbox')
        source_zip = root.parent / 'chatterbox-source.zip'
        urllib.request.urlretrieve(url, source_zip)
        source = root.parent / 'source'
        source.mkdir(exist_ok=True)
        with zipfile.ZipFile(source_zip) as package:
            for item in package.infolist():
                target = (source / item.filename).resolve()
                if not target.is_relative_to(source.resolve()):
                    raise ValueError('Ruta inválida en la descarga de código.')
            package.extractall(source)
        project = next(source.glob('*/pyproject.toml')).parent
        metadata = project / 'pyproject.toml'
        metadata.write_text(metadata.read_text().replace('git+https://github.com/resemble-ai/Perth.git@master', github_package('resemble-ai/Perth')), encoding='utf-8')
        pip(str(project), '--extra-index-url', 'https://download.pytorch.org/whl/cpu')

def wanted(engine, name):
    if name in ('README.md', 'LICENSE', 'LICENSE.txt'): return True
    if engine == 'Kokoro':
        return name in ('config.json', 'kokoro-v1_0.pth', 'VOICES.md') or name.startswith('voices/') and name.split('/')[1][:2] in ('af','am','bf','bm','ef','em')
    if engine == 'Pocket': return name.startswith(('languages/english/', 'languages/spanish/'))
    if engine == 'Nano': return name in ('added_tokens.json','conds.pt','merges.txt','s3gen_meanflow.safetensors','special_tokens_map.json','t3_nano_v1.safetensors','t3_nano_v1.yaml','tokenizer_config.json','ve.safetensors','vocab.json')
    return name in ('ve.pt','t3_mtl23ls_v3.safetensors','s3gen.pt','grapheme_mtl_merged_expanded_v1.json','conds.pt','Cangjie5_TC.json')

def main(engine, root):
    root.mkdir(parents=True, exist_ok=True)
    packages(engine, root)
    repo = {'Kokoro':'hexgrad/Kokoro-82M','Pocket':'kyutai/pocket-tts-without-voice-cloning','Nano':'ResembleAI/chatterbox-nano','ChatterboxMultilingual':'ResembleAI/chatterbox'}[engine]
    metadata = get_json('https://huggingface.co/api/models/' + repo + '?blobs=true')
    revision = metadata['sha']
    entries = [e for e in metadata['siblings'] if wanted(engine, e['rfilename'])]
    if not entries: raise RuntimeError('No se encontraron los archivos del modelo.')
    for index, entry in enumerate(entries):
        name = entry['rfilename']
        target = (root / name).resolve()
        if not target.is_relative_to(root.resolve()): raise ValueError('Ruta de modelo inválida.')
        target.parent.mkdir(parents=True, exist_ok=True)
        size = entry.get('size') or entry.get('lfs', {}).get('size')
        expected = entry.get('lfs', {}).get('sha256')
        if target.exists() and target.stat().st_size == size:
            if not expected or hashlib.file_digest(target.open('rb'), 'sha256').hexdigest() == expected: continue
        report('Descargando ' + name + '…', index / len(entries))
        partial = target.with_name(target.name + '.partial')
        digest = hashlib.sha256()
        request = urllib.request.Request(f'https://huggingface.co/{repo}/resolve/{revision}/{name}', headers={'User-Agent':'CaptionForge-SpeechModule'})
        with urllib.request.urlopen(request, timeout=120) as response, partial.open('wb') as output:
            count = 0
            while block := response.read(1024 * 1024):
                digest.update(block); output.write(block); count += len(block)
                report(f'{name}: {count // 1048576} MB', (index + (count / size if size else 0)) / len(entries))
        if size and partial.stat().st_size != size or expected and digest.hexdigest() != expected:
            raise RuntimeError('Descarga incompleta o SHA256 incorrecto: ' + name)
        partial.replace(target)
    # Verify imports before marking an installation as ready.
    if engine == 'Kokoro': from kokoro import KModel, KPipeline
    elif engine == 'Pocket': from pocket_tts import TTSModel
    elif engine == 'Nano': from chatterbox.tts_turbo import ChatterboxTurboTTS
    else: from chatterbox.mtl_tts import ChatterboxMultilingualTTS
    (root.parent / 'installed.json').write_text(json.dumps(dict(engine=engine, repository=repo, revision=revision, python=sys.executable)), encoding='utf-8')
    report('Motor instalado y listo.', 1)

if __name__ == '__main__':
    main(sys.argv[1], Path(sys.argv[2]))
