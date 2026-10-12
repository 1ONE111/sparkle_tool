"""실제 인스타 응답 점검 (GitHub Actions 'Insta diagnose' 워크플로에서 실행).

    python tools/insta_diagnose.py [아이디 또는 링크 ...]     인자가 없으면 tools/insta_accounts.txt

1) 처음 몇 계정은 응답을 그대로 들여다본다 (상태 코드, 키, 게시물 항목 키)
2) 전체 계정을 피드 · 릴스 기준으로 수집해 표로 출력하고 엑셀 두 개를 저장한다
   (비로그인은 두 기준 모두 같은 프로필 응답을 쓰므로 계정당 요청은 한 번)
"""

import json
import os
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from insta_crawler.client import BASE, InstaClient, InstaError  # noqa: E402
from insta_crawler.collector import COLUMNS, MODES, TITLES, Runner, StopRun, notes  # noqa: E402
from insta_crawler.config import Settings, app_data_dir, load_session  # noqa: E402
from insta_crawler.files import default_filename, export_xlsx  # noqa: E402
from insta_crawler.parsing import parse_targets, profile_url, reels_url  # noqa: E402

PEEK = 3
NODE_KEYS = ("shortcode", "taken_at_timestamp", "is_video", "product_type", "edge_liked_by", "edge_media_preview_like",
             "edge_media_to_comment", "video_view_count", "video_play_count", "pinned_for_users")


class CachedClient(InstaClient):
    """같은 계정 프로필은 한 번만 요청한다 (피드 · 릴스 두 번 돌릴 때)."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.cache: dict[str, dict] = {}

    def profile(self, username: str) -> dict:
        if username not in self.cache:
            self.cache[username] = super().profile(username)
        return self.cache[username]


def peek(client: InstaClient, username: str) -> None:
    url = f"{BASE}/api/v1/users/web_profile_info/?username={username}"
    resp = client.session.get(url, headers={"Referer": f"{BASE}/{username}/"}, timeout=15, allow_redirects=False)
    print(f"- @{username}: HTTP {resp.status_code} {resp.headers.get('content-type', '')} len={len(resp.text)}"
          f" location={resp.headers.get('Location', '')}")
    try:
        data = resp.json()
    except ValueError:
        print("    (JSON 아님) " + resp.text[:300].replace("\n", " "))
        return
    user = (data.get("data") or {}).get("user") if isinstance(data, dict) else None
    if not isinstance(user, dict):
        print("    " + json.dumps(data, ensure_ascii=False)[:400])
        return
    media = user.get("edge_owner_to_timeline_media") or {}
    edges = media.get("edges") or []
    print(f"    followers={user.get('edge_followed_by')} private={user.get('is_private')} id={user.get('id')}"
          f" media_count={media.get('count')} edges={len(edges)}")
    for edge in edges[:4]:
        node = edge.get("node") or {}
        shown = {k: node.get(k) for k in NODE_KEYS if k in node}
        if isinstance(shown.get("pinned_for_users"), list):
            shown["pinned_for_users"] = len(shown["pinned_for_users"])
        print("    node " + json.dumps(shown, ensure_ascii=False))
    if edges:
        extra = sorted(k for k in (edges[0].get("node") or {}) if "count" in k or "view" in k or "play" in k)
        print(f"    count/view/play 키: {extra}")


def table(mode: str, rows: list[dict]) -> None:
    cols = [c for c in COLUMNS[mode] if c.key not in ("name", "url")]
    print(f"\n=== {TITLES[mode]} ===")
    print(" | ".join(c.header for c in cols) + " | 상태 | 비고")
    for row in rows:
        cells = []
        for c in cols:
            value = row.get(c.key)
            cells.append(f"{value:,}" if isinstance(value, int) else ("-" if value in (None, "") else str(value)))
        print(" | ".join(cells) + f" | {row['status']} | {row['message']}")


def main(args: list[str]) -> int:
    text = "\n".join(args) if args else (Path(__file__).with_name("insta_accounts.txt")).read_text(encoding="utf-8")
    targets = parse_targets(text)
    cookies = load_session()
    client = CachedClient(cookies)
    print(f"계정 {len(targets)}개 · {'로그인' if client.logged_in else '비로그인'}")

    print("\n=== 1) 응답 들여다보기 ===")
    for target in targets[:PEEK]:
        try:
            peek(client, target.username)
        except Exception as exc:  # noqa: BLE001
            print(f"- @{target.username}: {exc.__class__.__name__} {exc}")

    print("\n=== 2) 수집 ===")
    settings = Settings(request_delay=float(os.environ.get("INSTA_DELAY", "3")), cooldown=60)
    out_dir = app_data_dir() / "out"
    out_dir.mkdir(parents=True, exist_ok=True)
    code = 0
    for mode in MODES:
        settings.mode = mode
        if mode != "feed":
            settings.request_delay = 0.2  # 비로그인은 프로필 캐시를 다시 쓰므로 거의 요청이 없다
        results: dict[int, object] = {}
        try:
            Runner(settings, client, print).run(list(enumerate(targets)), threading.Event(),
                                                on_result=results.__setitem__)
        except StopRun as exc:
            print(f"[멈춤] {exc}")
            code = 1
        rows = []
        for i, target in enumerate(targets):
            res = results.get(i)
            name = target.username or target.raw
            url = (reels_url if mode == "reels" else profile_url)(name)
            if res is None:
                rows.append({"username": name, "url": url, "status": "대기", "message": "미수집"})
            else:
                rows.append({"username": name, "url": url, **res.values, "status": res.status, "message": res.message})
        table(mode, rows)
        saved = export_xlsx(out_dir / default_filename(MODES[mode]), TITLES[mode], COLUMNS[mode], rows,
                            notes(mode, settings.posts))
        print(f"저장: {saved}")
    return code


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except InstaError as exc:
        print(f"[오류] {exc}")
        raise SystemExit(1)
