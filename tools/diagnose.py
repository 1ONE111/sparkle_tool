"""실제 네이버 응답 진단 (GitHub Actions 'Diagnose' 워크플로에서 실행).

    python tools/diagnose.py <블로그ID 또는 포스팅 URL 또는 카페 게시글 URL> ...

1) 크롬 없이 요청했을 때 각 주소가 무엇을 돌려주는지 (상태 코드, 핵심 키 주변 글자)
2) 실제 크롤러로 블로그 정보 / 블로그 포스팅을 수집한 결과

카페 메인 주소는 카페 정보 · 평균 조회수, 지식iN 주소는 지식iN 결과만 출력한다.
'kin' 만 넣으면 지식iN 목록에서 질문 주소 몇 개를 골라 수집한다.
"""

import json
import re
from html import unescape
import sys
import threading
import time
from pathlib import Path

import requests

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from naver_crawler.config import Settings  # noqa: E402
from naver_crawler.crawlers import TASK_BY_ID, Context, extract_read_counts, resolve_club_id  # noqa: E402
from naver_crawler.engine import Engine  # noqa: E402
from naver_crawler.http import DEFAULT_UA  # noqa: E402
from naver_crawler.parsing import (  # noqa: E402
    find_key, load_json_loose, parse_blog_url, parse_cafe_url, walk_dicts,
)
from naver_crawler.selectors import load_selectors  # noqa: E402

PATTERNS = [
    r'"(?:subscriberCount|buddyCount|neighborCount|followerCount|nickName|nickname|blogName|displayNickName|dayVisitorCount|totalVisitorCount)"\s*:\s*"?[^,"}]{0,40}',
    r"nickNameArea[^<]{0,80}<?[^<]{0,80}",
    r".{0,60}(?:이웃|구독자).{0,60}",
    r"visitorcnt[^>]{0,60}",
]


POST_PATTERNS = [
    r'"(?:sympathyCount|likeItCount|likeCount|commentCount|replyCount|commentCnt|sympathyCnt|blogNo|isSympathyAllowed|commentOpen|isCommentAllowed|allowComment)"\s*:\s*"?[^,"}]{0,40}',
    r'id="(?:commentCount|Comi\d+|Sympathy\d+)"[^>]{0,120}>[^<]{0,30}',
    r'class="[^"]*(?:u_cnt|_count|_commentCount|btn_comment|area_sympathy|u_likeit)[^"]*"[^>]{0,80}>[^<]{0,30}',
    r'.{0,40}(?:공감|댓글)\s*<?[^>]{0,40}>?\s*\d[\d,]*.{0,20}',
    r'(?:sympathy|commentCount|replyCount)[A-Za-z]*\s*[=:]\s*["\']?\d+',
    # 작성일
    r'class="[^"]*(?:publishDate|blog_date|se_date|_postAddDate|date)[^"]*"[^>]{0,80}>[^<]{0,40}',
    r'"(?:addDate|publishDate|writeDate|postDate|logDate|addTimestamp)"\s*:\s*"?[^,"}]{0,40}',
]


def show(session: requests.Session, name: str, url: str, referer: str | None = None,
         patterns: list[str] | None = None, raw: bool = False) -> str:
    try:
        r = session.get(url, headers={"Referer": referer} if referer else {}, timeout=15)
    except requests.RequestException as exc:
        print(f"  [{name}] ERROR {exc}")
        return ""
    body = r.text
    print(f"  [{name}] {r.status_code} {r.headers.get('content-type', '')} len={len(body)} -> {r.url}")
    if raw:
        print("      " + re.sub(r"\s+", " ", body[:900]))
        return body
    hits = []
    for pattern in patterns or PATTERNS:
        for m in re.finditer(pattern, body):
            snippet = re.sub(r"\s+", " ", m.group(0))[:160]
            if snippet not in hits:
                hits.append(snippet)
            if len(hits) >= 12:
                break
    for h in hits[:12]:
        print(f"      · {h}")
    if not hits:
        print("      (키 없음) " + re.sub(r"\s+", " ", body[:300]))
    return body


def show_dates(name: str, body: str) -> None:
    """작성일이 들어 있을 만한 곳 (태그 포함 앞뒤 글자)."""
    rx = r'(?:publishDate|blog_date|se_date|_postAddDate|addDate|"date")'
    found = 0
    for m in re.finditer(rx, body):
        print(f"      [{name} 작성일] " + re.sub(r"\s+", " ", body[max(0, m.start() - 60):m.end() + 240]))
        found += 1
        if found >= 4:
            break
    for m in list(re.finditer(r"20\d\d\.\s?\d{1,2}\.\s?\d{1,2}\.?\s?\d{1,2}:\d{2}", body))[:3]:
        print(f"      [{name} 날짜형식] " + re.sub(r"\s+", " ", body[max(0, m.start() - 120):m.end() + 20]))


def diagnose_posts(s: requests.Session, urls: list[str]) -> None:
    print("=== 포스팅: 크롬 없이 요청 ===")
    for url in urls:
        ref = parse_blog_url(url)
        bid, no = ref.blog_id, ref.log_no
        print(f"- {bid}/{no}")
        post_view = f"https://blog.naver.com/PostView.naver?blogId={bid}&logNo={no}"
        pc = show(s, "pc_postview", post_view, f"https://blog.naver.com/{bid}", POST_PATTERNS)
        show_dates("pc_postview", pc)
        mobile = show(s, "m_post", f"https://m.blog.naver.com/{bid}/{no}", None, POST_PATTERNS)
        show_dates("m_post", mobile)
        show(s, "like_api", f"https://blog.like.naver.com/v1/search/contents?suppress_response_codes=true"
             f"&q=BLOG%5B{bid}_{no}%5D&isDuplication=true", post_view, raw=True)
        info = load_json_loose(show(s, "rego", f"https://m.blog.naver.com/rego/BlogInfo.naver?blogId={bid}",
                                    f"https://m.blog.naver.com/{bid}", [r'"blogNo"\s*:\s*\d+']))
        blog_no = find_key(info, ["blogNo"]) if info else None
        if blog_no:
            show(s, "cbox", "https://apis.naver.com/commentBox/cbox/web_naver_list_jsonp.json?ticket=blog"
                 f"&templateId=default&pool=blogid&lang=ko&country=&objectId={blog_no}_201_{no}"
                 f"&pageSize=1&indexSize=10&groupId={blog_no}&listType=OBJECT&pageType=default&_callback=cb",
                 post_view, raw=True)
        show(s, "m_api_post", f"https://m.blog.naver.com/api/blogs/{bid}/posts/{no}", f"https://m.blog.naver.com/{bid}/{no}", raw=True)

    print("\n=== 포스팅: 크롤러 ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15, save_debug_html=True)
    engine = Engine(settings, lambda msg: print(f"  log: {msg}"))
    results = {}
    try:
        engine.run(TASK_BY_ID["blog_post"], list(enumerate(urls)), threading.Event(),
                   on_result=lambda k, r: results.__setitem__(k, r))
    finally:
        engine.shutdown()
    for i, url in enumerate(urls):
        r = results.get(i)
        print(f"- {url}: {r.status if r else '-'} {r.values if r else ''} {r.message if r else ''}")


CAFE_KEYS = [r'"(?:commentCount|readCount|likeItCount|likeCount|totalCount|isOpen|openArticle|isReadable|'
             r'errorCode|code|reason|message|isMember|memberLevel|commentReadable|isCommentable)"\s*:\s*"?[^,"}]{0,60}']

# 댓글 숫자가 화면 어디에 있는지 (선택자별 글자 + 태그)
_JS_DUMP = r"""
const out = [];
for (const s of arguments[0]) {
  let els; try { els = document.querySelectorAll(s); } catch (e) { continue; }
  let i = 0;
  for (const el of els) {
    if (i++ >= 3) break;
    const t = ((el.innerText || '').trim() || (el.textContent || '').trim()).replace(/\s+/g, ' ').slice(0, 40);
    out.push(s + '  =>  "' + t + '"  |  ' + el.outerHTML.replace(/\s+/g, ' ').slice(0, 220));
  }
}
return out;
"""
EXTRA_COMMENT_SELECTORS = [".button_comment", "[class*='comment'] [class*='num']", "[class*='Comment'] [class*='count']",
                           ".CommentBox", ".comment_box", ".ArticleBottomBtns"]


def diagnose_cafe(s: requests.Session, urls: list[str]) -> None:
    print("=== 카페 게시글: 크롬 없이 요청 (로그인 안 한 상태) ===")
    for url in urls:
        ref = parse_cafe_url(url)
        print(f"- {url}")
        club_id = ref.club_id
        if not club_id and ref.slug:
            gate = load_json_loose(show(s, "cafe_gate", f"https://apis.naver.com/cafe-web/cafe2/CafeGateInfo.json?cluburl={ref.slug}",
                                        "https://cafe.naver.com/", [r'"(?:cafeId|clubId|clubid|cafeName|isPublic|openType)"\s*:\s*"?[^,"}]{0,40}']))
            club_id = find_key(gate, ["cafeId", "clubId", "clubid"]) if gate else None
        if not club_id or not ref.article_id:
            print("      카페 ID / 글 번호 없음")
            continue
        show(s, "article_api", f"https://apis.naver.com/cafe-web/cafe-articleapi/v2.1/cafes/{club_id}/articles/{ref.article_id}"
             "?query=&useCafeId=true&requestFrom=A", "https://cafe.naver.com/", CAFE_KEYS)
        show(s, "comments_api", f"https://apis.naver.com/cafe-web/cafe-articleapi/v2/cafes/{club_id}/articles/{ref.article_id}"
             "/comments/pages/1?requestFrom=A&orderBy=asc", "https://cafe.naver.com/", CAFE_KEYS)

    print("\n=== 카페 게시글: 크롤러 (창 없는 크롬, 로그인 안 함) ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15, save_debug_html=True)
    engine = Engine(settings, lambda msg: print(f"  log: {msg}"))
    results = {}
    try:
        engine.run(TASK_BY_ID["cafe_article"], list(enumerate(urls)), threading.Event(),
                   on_result=lambda k, r: results.__setitem__(k, r))
        for i, url in enumerate(urls):
            r = results.get(i)
            print(f"- {url}: {r.status if r else '-'} {r.values if r else ''} {r.message if r else ''}")

        print("\n=== 카페 게시글: 화면에서 댓글 숫자 위치 ===")
        sel = load_selectors()["cafe_article"]
        ctx = Context(engine.browser, engine.http, settings, load_selectors(), threading.Event(), lambda m: None)
        for url in urls:
            print(f"- {url}")
            page = ctx.page()
            page.open(url, sel.get("frames", ["cafe_main"]))
            page.wait_until(lambda: page.count(sel["views"]) > 0, timeout=12)
            time.sleep(3)
            page.enter_frame()
            print(f"      현재 주소: {page.driver.current_url}")
            for line in page.driver.execute_script(_JS_DUMP, sel["comments"] + EXTRA_COMMENT_SELECTORS) or []:
                print(f"      · {line}")
            body = page.body_text()
            for m in list(re.finditer(r".{0,30}댓글.{0,30}", body))[:6]:
                print(f"      [본문] {m.group(0)!r}")
            for resp in page.captured_json():
                found = find_key(resp.data, ["commentCount"])
                if found is not None:
                    print(f"      [응답] commentCount={found} <- {resp.url[:140]}")
                if "like" in resp.url.lower():
                    print(f"      [좋아요 응답] {resp.url[:160]}")
                    print("         " + json.dumps(resp.data, ensure_ascii=False)[:500])
            for line in page.driver.execute_script(_JS_DUMP, sel["likes"] + [".like_article"]) or []:
                print(f"      · {line}")
    finally:
        engine.shutdown()


def crawl(task_id: str, urls: list[str]) -> None:
    """크롤러 결과만 한 줄씩 (창 없는 크롬, 로그인 안 함)."""
    print(f"\n=== {TASK_BY_ID[task_id].title}: 크롤러 ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15, save_debug_html=True)
    engine = Engine(settings, lambda msg: print(f"  log: {msg}"))
    results = {}
    try:
        engine.run(TASK_BY_ID[task_id], list(enumerate(urls)), threading.Event(),
                   on_result=lambda k, r: results.__setitem__(k, r))
    finally:
        engine.shutdown()
    for i, url in enumerate(urls):
        r = results.get(i)
        print(f"- {url}: {r.status if r else '-'} {r.values if r else ''} {r.message if r else ''}")


def inspect_cafe_main(s: requests.Session, urls: list[str]) -> None:
    """카페 정보(회원수 칸 · memberCount 응답)와 평균 조회수용 목록 API 응답."""
    print("\n=== 카페 메인: 회원수 위치 · 목록 API ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15)
    engine = Engine(settings, lambda msg: None)
    sel = load_selectors()
    ctx = Context(engine.browser, engine.http, settings, sel, threading.Event(), lambda m: None)
    try:
        for url in urls:
            print(f"- {url}")
            page = ctx.page()
            page.open(url, [])
            time.sleep(4)
            page.enter_frame()
            print(f"      현재 주소: {page.driver.current_url}")
            for line in page.driver.execute_script(_JS_DUMP, sel["cafe_info"]["members"]) or []:
                print(f"      · {line}")
            for m in list(re.finditer(r".{0,30}(?:멤버|회원|비공개).{0,30}", page.body_text()))[:8]:
                print(f"      [본문] {m.group(0)!r}")
            for resp in page.captured_json():
                for d in walk_dicts(resp.data):
                    if "memberCount" in d:
                        keys = {k: v for k, v in d.items() if not isinstance(v, (dict, list))}
                        print(f"      [응답] {resp.url[:120]}")
                        print("         " + json.dumps(keys, ensure_ascii=False)[:600])
                        break
            club_id = resolve_club_id(ctx, parse_cafe_url(url), page)
            if not club_id:
                continue
            for name, api in (
                ("list_v2.1", f"https://apis.naver.com/cafe-web/cafe2/ArticleListV2dot1.json?search.clubid={club_id}"
                              "&search.queryType=lastArticle&search.page=10&search.perPage=15&search.boardtype=L&ad=false"),
                ("boardlist", f"https://apis.naver.com/cafe-web/cafe-boardlist-api/v1/cafes/{club_id}/menus/0/articles"
                              "?page=10&pageSize=15&sortBy=TIME&viewType=L"),
            ):
                data = load_json_loose(show(s, name, api, f"https://cafe.naver.com/f-e/cafes/{club_id}/menus/0", raw=True))
                print(f"      [{name} 조회수] {extract_read_counts(data) if data is not None else None}")
                first = next((d for d in walk_dicts(data) if "readCount" in d), None) if data is not None else None
                if first:
                    print("      [첫 글] " + json.dumps({k: v for k, v in first.items() if not isinstance(v, (dict, list))},
                                                     ensure_ascii=False)[:600])
    finally:
        engine.shutdown()


def inspect_kin(urls: list[str]) -> None:
    """지식iN 제목 · 조회수 선택자가 화면에서 무엇을 잡는지, 조회수가 보이기까지 걸린 시간."""
    print("\n=== 지식iN: 화면 선택자 ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15)
    engine = Engine(settings, lambda msg: None)
    sel = load_selectors()["kin"]
    ctx = Context(engine.browser, engine.http, settings, load_selectors(), threading.Event(), lambda m: None)
    try:
        for url in urls:
            print(f"- {url}")
            page = ctx.page()
            started = time.monotonic()
            page.open(url, [])
            found = page.wait_until(lambda: page.text(sel["views"], need_digit=True, contains=["조회"]) is not None,
                                    timeout=15)
            print(f"      조회수 선택자 {'찾음' if found else '못 찾음'} ({time.monotonic() - started:.1f}초)")
            for line in page.driver.execute_script(_JS_DUMP, sel["title"] + sel["views"]) or []:
                print(f"      · {line}")
            for m in list(re.finditer(r".{0,40}조회.{0,30}", page.body_text()))[:5]:
                print(f"      [본문] {m.group(0)!r}")
    finally:
        engine.shutdown()


def sample_kin_urls(s: requests.Session, count: int = 3) -> list[str]:
    """지식iN 목록에서 질문 주소 몇 개 (인자로 'kin' 만 넣었을 때)."""
    for page in ("https://kin.naver.com/qna/list.naver", "https://kin.naver.com/"):
        try:
            html = s.get(page, timeout=15).text
        except requests.RequestException:
            continue
        found = []
        for m in re.finditer(r"/qna/detail\.naver\?[^\"'\s<>]*?docId=\d+", html):
            url = "https://kin.naver.com" + unescape(m.group(0))
            if url not in found:
                found.append(url)
        if found:
            return found[:count]
    print("  지식iN 목록에서 질문 주소를 찾지 못했습니다")
    return []


def main(args: list[str]) -> None:
    s = requests.Session()
    s.headers.update({"User-Agent": DEFAULT_UA, "Accept-Language": "ko-KR,ko;q=0.9"})
    kins = [a for a in args if "kin.naver.com" in a]
    if "kin" in args:
        kins += sample_kin_urls(s)
    args = [a for a in args if a != "kin" and "kin.naver.com" not in a]
    cafe_urls = [a for a in args if "cafe.naver.com" in a]
    cafes = [a for a in cafe_urls if parse_cafe_url(a).article_id]
    cafe_mains = [a for a in cafe_urls if a not in cafes]
    posts = [a for a in args if a.startswith("http") and a not in cafe_urls]
    ids = [a for a in args if not a.startswith("http")]
    if cafe_mains:
        crawl("cafe_info", cafe_mains)
        crawl("cafe_avg", cafe_mains)
        inspect_cafe_main(s, cafe_mains)
    if kins:
        crawl("kin", kins)
        inspect_kin(kins)
    if cafes:
        diagnose_cafe(s, cafes)
    if posts:
        diagnose_posts(s, posts)
    if not ids:
        return
    print("=== 1) 크롬 없이 요청 ===")
    for blog_id in ids:
        print(f"- {blog_id}")
        show(s, "visitor", f"https://blog.naver.com/NVisitorgp4Ajax.nhn?blogId={blog_id}", f"https://blog.naver.com/{blog_id}")
        show(s, "rego", f"https://m.blog.naver.com/rego/BlogInfo.naver?blogId={blog_id}", f"https://m.blog.naver.com/{blog_id}")
        show(s, "api_blogs", f"https://m.blog.naver.com/api/blogs/{blog_id}", f"https://m.blog.naver.com/{blog_id}")
        show(s, "pc_main", f"https://blog.naver.com/{blog_id}")
        show(s, "pc_postlist", f"https://blog.naver.com/PostList.naver?blogId={blog_id}", f"https://blog.naver.com/{blog_id}")
        show(s, "m_main", f"https://m.blog.naver.com/{blog_id}")

    print("\n=== 2) 크롤러(창 없는 크롬) ===")
    settings = Settings(headless=True, keep_login=False, request_delay=0.5, page_timeout=15, save_debug_html=True)
    engine = Engine(settings, lambda msg: print(f"  log: {msg}"))
    results = {}
    try:
        engine.run(TASK_BY_ID["blog_info"], [(i, f"https://blog.naver.com/{b}") for i, b in enumerate(ids)],
                   threading.Event(), on_result=lambda k, r: results.__setitem__(k, r))
    finally:
        engine.shutdown()
    for i, blog_id in enumerate(ids):
        r = results.get(i)
        print(f"- {blog_id}: {r.status if r else '-'} {r.values if r else ''} {r.message if r else ''}")


if __name__ == "__main__":
    main(sys.argv[1:] or ["shriya"])
