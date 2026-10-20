from pathlib import Path
root=Path(__file__).resolve().parent
body=(root/'MODDB_DESCRIPTION.html.in').read_text(encoding='utf-8')
(root/'MODDB_DESCRIPTION.html').write_text(body,encoding='utf-8',newline='\n')
preview="""<!doctype html><html lang="en"><meta charset="utf-8"><title>ModDB description preview</title>
<style>body{max-width:900px;margin:32px auto;padding:0 20px;font:17px/1.5 system-ui;color:#20251f}button{padding:10px;margin:8px 8px 16px 0}aside{padding:12px;background:#edf1e7}</style>
<aside>Local release-description preview. Copy only the description below; this toolbar is outside the copied body.</aside>
<button id="copy">Copy formatted description</button><button id="select">Select description</button><span id="status"></span>
<article id="description">"""+body+"""</article><script>
const body=document.querySelector('#description');
document.querySelector('#select').onclick=()=>{const range=document.createRange();range.selectNodeContents(body);const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);};
document.querySelector('#copy').onclick=async()=>{try{await navigator.clipboard.write([new ClipboardItem({'text/html':new Blob([body.innerHTML],{type:'text/html'}),'text/plain':new Blob([body.innerText],{type:'text/plain'})})]);document.querySelector('#status').textContent='Copied';}catch{document.querySelector('#select').click();document.querySelector('#status').textContent='Selected; use Ctrl+C';}};
</script></html>"""
(root/'MODDB_PREVIEW.html').write_text(preview,encoding='utf-8',newline='\n')
