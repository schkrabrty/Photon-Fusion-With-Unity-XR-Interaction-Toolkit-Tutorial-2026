"""Render exact, syntax-highlighted excerpts from this repository as README PNGs.
Install pygments and playwright, then run: python -m playwright install chromium
Run this script from any directory. No Unity project files are modified.
"""
from pathlib import Path
import html
import json
import os
import textwrap
from pygments import highlight
from pygments.lexers import CSharpLexer
from pygments.formatters import HtmlFormatter
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = "Assets/Fusion and Essential Spawned Player Stuffs/Scripts/"
# filename, title, source, first included line, last included line, teaching caption
EXCERPTS = [
    ("code-join", "01 / CONNECT & JOIN", SCRIPTS + "FusionNetworkManager.cs",
     "        var startGameArgs", "        };",
     "One Shared session. One room name. The Lobby is registered with Fusion."),
    ("code-countdown", "02 / ONE SHARED COUNTDOWN", SCRIPTS + "FusionLobbyState.cs",
     "        if (count < RequiredPlayers)", "        if (Countdown.Expired(Runner)",
     "Wait below the minimum. Shorten at capacity. Load Game when the timer expires."),
    ("code-grab", "03 / WHO MAY MOVE THE CUBE?", "Assets/Scripts/XRGrabNetworkInteractable.cs",
     "    private void OnSelectEntered", "        if (Object.HasStateAuthority) Object.ReleaseStateAuthority();",
     "XRI handles the grab. Fusion authority determines whose transform is replicated."),
    ("code-networked", "04 / REMEMBER A VALUE", SCRIPTS + "FusionNetworkPlayer.cs",
     "    [Networked, OnChangedRender", "    public Color PlayerColor { get; set; }",
     "The color is networked state. ApplyPlayerColor turns that state into a visible color."),
    ("code-rpc", "05 / SEND A MESSAGE", "Assets/Scripts/XRSimpleNetworkInteractable.cs",
     "    [Rpc(RpcSources.All, RpcTargets.All)]", '        if (txtInfo) txtInfo.text = color.Equals(_originalColor) ? "Tap the cube!" : "";',
     "This RPC changes the current peers' visuals. It does not store a persistent color."),
    ("code-cube-lesson", "06 / REQUEST → STATE → EFFECT", "docs/examples/CubeColorLesson.cs",
     "    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]", "        if (celebration) celebration.Play();",
     "Optional exercise: the authority stores the color, then broadcasts a celebration."),
]

def main():
    output = ROOT / "docs/images"
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    formatter = HtmlFormatter(nowrap=True, style="github-dark")
    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=os.environ.get("CHROMIUM_PATH"))
        page = browser.new_page(viewport={"width": 1240, "height": 1000}, device_scale_factor=2)
        for name, title, source, first, last, caption in EXCERPTS:
            lines = (ROOT / source).read_text(encoding="utf-8-sig").splitlines()
            start = next(i for i, line in enumerate(lines) if line.startswith(first))
            end = next(i for i in range(start, len(lines)) if lines[i].startswith(last)) + 1
            # Include the method's closing brace for excerpts that end at a statement.
            if name in {"code-grab", "code-rpc", "code-cube-lesson"}: end += 1
            snippet = textwrap.dedent("\n".join(lines[start:end]))
            code = highlight(snippet, CSharpLexer(), formatter)
            numbers = "\n".join(map(str, range(start + 1, end + 1)))
            content = f'''<!doctype html><html><meta charset="utf-8"><style>
            *{{box-sizing:border-box}}body{{margin:0;padding:28px;background:#eef3f7;color:#e6edf3;font-family:Arial,sans-serif}}
            article{{border-radius:18px;overflow:hidden;background:#101820;border:1px solid #263646}}
            header{{padding:24px 30px;border-bottom:1px solid #2d3d4b;display:flex;justify-content:space-between;align-items:center}}
            strong{{color:#7be0ce;font-size:17px;letter-spacing:1.4px}}header span{{font-size:17px;color:#acbbca}}
            section{{padding:28px 26px;display:flex;gap:24px}}pre{{font-family:Menlo,Consolas,monospace;font-size:17px;line-height:1.7;margin:0;white-space:pre}}
            .numbers{{color:#617484;text-align:right;user-select:none}}footer{{padding:20px 30px;background:#192631;color:#c2d0dc;font-size:17px;line-height:1.4}}
            {formatter.get_style_defs('.code')}
            </style><article><header><strong>{html.escape(title)}</strong><span>{html.escape(Path(source).name)}</span></header>
            <section><pre class="numbers">{numbers}</pre><pre class="code">{code}</pre></section><footer>{html.escape(caption)}</footer></article></html>'''
            page.set_content(content)
            width=page.evaluate("document.documentElement.scrollWidth")
            if width>1240: page.set_viewport_size({"width":width,"height":1000})
            page.locator("article").screenshot(path=str(output / (name + ".png")))
            manifest.append({"image":name+".png","source":source,"first_line":start+1,"last_line":end,"excerpt":snippet})
            page.set_viewport_size({"width":1240,"height":1000})
        browser.close()
    (output / "code-excerpts.json").write_text(json.dumps(manifest, indent=2)+"\n")
    print(f"Rendered {len(manifest)} script excerpts.")

if __name__ == "__main__": main()
