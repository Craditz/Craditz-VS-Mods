"""Render editable description copy without external packages."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

def render():
    count=0
    for source in sorted((ROOT/'mods').rglob('DESCRIPTION.html.in')):
        body=source.read_text(encoding='utf-8')
        assert body.count('<em>mod was made with AI assistance. Pls no hate.</em>')==1,source
        (source.parent/'MODDB_DESCRIPTION.html').write_text(body,encoding='utf-8')
        preview='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>notes from the stolen computer</title><style>body{max-width:900px;margin:3rem auto;padding:0 1.5rem;background:#191b18;color:#eee8d5;font:17px/1.6 Georgia,serif}a{color:#d9a76d}button{font:inherit;padding:.6rem 1rem;cursor:pointer}article{margin-top:2rem}code{font-size:.85em}</style><p>the fox has left the copy button outside the description.</p><button onclick="copyDescription()">Copy formatted description</button><p id="status" aria-live="polite"></p><article id="description">'''+body+'''</article><script>async function copyDescription(){const el=document.getElementById('description');const status=document.getElementById('status');try{await navigator.clipboard.write([new ClipboardItem({'text/html':new Blob([el.innerHTML],{type:'text/html'}),'text/plain':new Blob([el.innerText],{type:'text/plain'})})]);status.textContent='copied. the pawprints are intentional.';}catch(e){const selection=window.getSelection();const range=document.createRange();range.selectNodeContents(el);selection.removeAllRanges();selection.addRange(range);status.textContent='the description is selected. press Ctrl+C to copy.';}}</script></html>'''
        (source.parent/'MODDB_PREVIEW.html').write_text(preview,encoding='utf-8')
        count+=1
    print('Rendered',count,'description/preview pairs')

if __name__=='__main__':render()
