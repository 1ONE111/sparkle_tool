from pathlib import Path
root=Path(__file__).parent
source=root/'BlogSearcher.cs'
text=source.read_text(encoding='utf-8')
start=text.index('  public MainForm() {')
end=text.index('  void Open(',start)
source.write_text(text[:start]+(root/'UiConstructor.txt').read_text(encoding='utf-8')+text[end:],encoding='utf-8')
