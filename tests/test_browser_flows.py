"""실제 크롬으로 가짜 네이버 페이지를 열어 수집 흐름을 검증한다.

NC_TEST_CHROME (chrome 실행 파일), NC_TEST_DRIVER (chromedriver) 환경 변수가 있을 때만 실행.
"""

import os
import threading
import time

import pytest

from naver_crawler.config import Settings
from naver_crawler.crawlers import TASK_BY_ID
from naver_crawler.engine import Engine
from tests.fake_naver import FakeHttp, FakeNaver

CHROME = os.environ.get("NC_TEST_CHROME")
DRIVER = os.environ.get("NC_TEST_DRIVER")
pytestmark = pytest.mark.skipif(not (CHROME and DRIVER), reason="NC_TEST_CHROME / NC_TEST_DRIVER 미설정")


@pytest.fixture(scope="module")
def engine(tmp_path_factory, monkeypatch_module):
    home = tmp_path_factory.mktemp("home")
    monkeypatch_module.setenv("NAVER_CRAWLER_HOME", str(home))
    with FakeNaver(tmp_path_factory.mktemp("cert")) as fake:
        monkeypatch_module.setenv("NAVER_CRAWLER_CHROME_ARGS", fake.chrome_args())
        settings = Settings(headless=True, keep_login=False, request_delay=0, page_timeout=5,
                            chromedriver_path=DRIVER, chrome_binary=CHROME)
        eng = Engine(settings)
        yield eng
        eng.shutdown()


@pytest.fixture(scope="module")
def monkeypatch_module():
    mp = pytest.MonkeyPatch()
    yield mp
    mp.undo()


def run(engine, task_id, urls, http=None):
    engine.http = http or FakeHttp()
    results = {}
    engine.run(TASK_BY_ID[task_id], list(enumerate(urls)), threading.Event(),
               on_result=lambda key, res: results.__setitem__(key, res))
    return [results[i] for i in range(len(urls))]


def test_cafe_info_old_layout(engine):
    (res,) = run(engine, "cafe_info", ["https://cafe.naver.com/testcafe"])
    assert res.status == "완료", res.message
    assert res.values["name"] == "테스트카페"
    assert res.values["members"] == 12345
    assert res.values["club_id"] == "111"


def test_cafe_info_member_list_lock_is_not_private_count(engine):
    """숫자 옆 자물쇠 '비공개' 는 멤버 목록 비공개 - 회원수는 보이므로 비고에 '회원수 비공개' 를 쓰지 않는다."""
    (res,) = run(engine, "cafe_info", ["https://cafe.naver.com/lockcafe"])
    assert res.status == "완료", res.message
    assert res.values["members"] == 275752
    assert "비공개" not in res.message


GONE = (404, '{"result":{"errorCode":"4003","reason":"삭제되었거나 존재하지 않는 게시글입니다."}}')


def test_cafe_article_deleted_checked_before_browser(engine):
    """글 정보 API 가 4003(삭제)이면 크롬을 열지 않고 바로 '게시글 삭제'."""
    http = FakeHttp({
        "https://apis.naver.com/cafe-web/cafe2/CafeGateInfo.json?cluburl=gonecafe": '{"cafeId": 555, "cafeUrl": "gonecafe"}',
        "https://apis.naver.com/cafe-web/cafe-articleapi/v2.1/cafes/555/articles/999": GONE,
    })
    started = time.monotonic()
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/gonecafe/999"], http)
    assert res.status == "게시글 삭제", res.message
    assert all(v in (None, "") for v in res.values.values())
    assert time.monotonic() - started < 2


def test_cafe_article_deleted_redirect_to_main_takes_no_main_numbers(engine):
    """크롬으로 열었더니 카페 메인으로 넘어간 삭제 글: 메인의 '조회 340' 같은 숫자를 쓰지 않는다."""
    http = FakeHttp({"https://apis.naver.com/cafe-web/cafe-articleapi/v2.1/cafes/666/articles/777": GONE})
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/gonemain/777"], http)
    assert res.status == "게시글 삭제", res.message
    assert res.values.get("views") is None


def test_cafe_article_redirected_without_api_is_failure_not_main_numbers(engine):
    """글 정보 API 도 안 되는데 다른 화면으로 넘어가면 실패 (메인 화면 숫자 · 카페명을 글 값으로 쓰지 않음)."""
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/gonemain/777"])
    assert res.status == "실패", res.message
    assert res.values["views"] is None and res.values["title"] is None


def test_cafe_article_iframe_and_late_like(engine):
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/testcafe/123"])
    assert res.status == "완료", res.message
    assert res.values == {"title": "첫 글", "views": 1234, "comments": 5, "likes": 7,
                          "date": "2026.10.01. 12:00", "nickname": "요리왕"}


def test_cafe_article_comment_placeholder_zero_uses_article_json(engine):
    """화면이 댓글 · 좋아요를 0 으로 먼저 그려 둬도 응답의 숫자를 쓴다. 옆 글 응답(0)은 무시."""
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/slowcafe/789"])
    assert res.status == "완료", res.message
    assert res.values["comments"] == 2
    assert res.values["likes"] == 1  # 화면의 숨은 0 이 아니라 좋아요 응답, 댓글 좋아요(5)는 무시
    assert res.values["views"] == 254
    assert res.values["nickname"] == "느림보"


def test_cafe_article_new_layout_from_captured_json(engine):
    (res,) = run(engine, "cafe_article", ["https://cafe.naver.com/f-e/cafes/111/articles/456"])
    assert res.status == "완료", res.message
    assert res.values["views"] == 77  # 다른 글의 readCount(99999)를 잡으면 안 됨
    assert res.values["comments"] == 3
    assert res.values["likes"] == 9
    assert res.values["title"] == "신규 글"
    assert res.values["nickname"] == "신규작성자"


def test_cafe_avg_via_api(engine):
    api = '{"message":{"result":{"articleList":[{"articleId":1,"readCount":10},{"articleId":2,"readCount":30}]}}}'
    http = FakeHttp({"https://apis.naver.com/cafe-web/cafe2/ArticleListV2dot1.json": api})
    (res,) = run(engine, "cafe_avg", ["https://cafe.naver.com/f-e/cafes/333"], http)
    assert res.status == "완료", res.message
    assert (res.values["avg"], res.values["samples"], res.values["page"]) == (20, 2, 10)
    assert "search.page=10" in http.calls[0]


def test_cafe_avg_via_list_page_skips_notice(engine):
    (res,) = run(engine, "cafe_avg", ["https://cafe.naver.com/f-e/cafes/222"])
    assert res.status == "완료", res.message
    assert (res.values["avg"], res.values["samples"]) == (500, 3)


def test_blog_info_and_no_widget_does_not_stop(engine):
    visitors = "".join(f'<visitorcnt id="2026100{i}" cnt="{c}"/>' for i, c in enumerate([10, 20, 30, 40, 50], 1))
    http = FakeHttp({
        "https://blog.naver.com/NVisitorgp4Ajax.nhn?blogId=tester": f"<visitorcnts>{visitors}</visitorcnts>",
        "https://blog.naver.com/NVisitorgp4Ajax.nhn?blogId=nowidget": "<visitorcnts></visitorcnts>",
    })
    no_widget, ok = run(engine, "blog_info", ["https://blog.naver.com/nowidget", "https://blog.naver.com/tester"], http)
    assert no_widget.status == "위젯없음"
    assert no_widget.values["name"] == "위젯 없는 블로그"
    assert ok.status == "완료", ok.message
    assert ok.values == {"name": "테스트 블로그", "nickname": "테스터", "avg": 30}


def test_blog_visitor_average_uses_last_five_days(engine):
    days = [10, 20, 30, 40, 50, 60, 70]
    xml = "".join(f'<visitorcnt id="2026100{i}" cnt="{c}"/>' for i, c in enumerate(days, 1))
    (res,) = run(engine, "blog_info", ["https://blog.naver.com/tester"],
                 FakeHttp({"https://blog.naver.com/NVisitorgp4Ajax.nhn": f"<visitorcnts>{xml}</visitorcnts>"}))
    assert res.values["avg"] == 50  # (30+40+50+60+70)/5


def test_blog_post_zero_comments_is_zero_not_missing(engine):
    """댓글 칸이 비어 있으면 0, 공감은 숨은 반응 팝업의 0 이 아니라 하트 옆 숫자."""
    (res,) = run(engine, "blog_post", ["https://blog.naver.com/zero/1"])
    assert res.status == "완료", res.message
    assert res.values["comments"] == 0
    assert res.values["likes"] == 3


def test_blog_post(engine):
    (res,) = run(engine, "blog_post", ["https://m.blog.naver.com/tester/999"])
    assert res.status == "완료", res.message
    assert res.values == {"title": "포스팅 제목", "likes": 21, "comments": 4, "date": "2026. 10. 2. 9:00",
                          "nickname": "포스팅작성자"}


def test_kin(engine):
    (res,) = run(engine, "kin", ["https://kin.naver.com/qna/detail.naver?d1id=1&docId=2"])
    assert res.status == "완료", res.message
    assert res.values == {"title": "질문 제목", "views": 5678}


def test_kin_new_layout(engine):
    """새 지식iN 화면: 조회수를 바로 찾고 (시간 초과까지 기다리지 않음), 제목 앞 숨은 '질문' 은 뺀다."""
    started = time.monotonic()
    (res,) = run(engine, "kin", ["https://kin.naver.com/qna/detail.naver?d1id=7&docId=9"])
    assert res.status == "완료", res.message
    assert res.values == {"title": "질문 있어요 독감", "views": 1207}
    assert time.monotonic() - started < 4  # page_timeout(5초) 까지 기다리지 않는다


def test_missing_page_is_failure_not_crash(engine):
    (res,) = run(engine, "kin", ["https://kin.naver.com/qna/none"])
    assert res.status == "실패"


def test_stop_event_cancels(engine):
    stop = threading.Event()
    stop.set()
    seen = []
    finished = engine.run(TASK_BY_ID["kin"], [(0, "https://kin.naver.com/qna/detail.naver")], stop,
                          on_start=seen.append)
    assert finished is False and seen == []


def test_moved_blog_does_not_take_other_blogs_values(engine):
    """PC 주소가 다른 블로그로 넘어가면 그 화면의 닉네임/블로그명을 쓰지 않는다."""
    http = FakeHttp({"https://blog.naver.com/NVisitorgp4Ajax.nhn": widget_xml(5, 5, 5, 5, 5)})
    (res,) = run(engine, "blog_info", ["https://blog.naver.com/movedblog"], http)
    assert res.values.get("nickname") != "엉뚱한닉"
    assert res.values.get("name") != "엉뚱한 블로그"


def widget_xml(*counts):
    rows = "".join(f'<visitorcnt id="2026100{i}" cnt="{c}"/>' for i, c in enumerate(counts, 1))
    return f"<visitorcnts>{rows}</visitorcnts>"


def test_browser_start_failure_stops_whole_job(tmp_path, monkeypatch):
    """크롬을 못 띄우면 URL 마다 실패를 쌓지 않고 첫 줄에서 바로 멈춘다."""
    from naver_crawler.browser import BrowserError

    monkeypatch.setenv("NAVER_CRAWLER_HOME", str(tmp_path))
    eng = Engine(Settings(headless=True, keep_login=False, request_delay=0,
                          chromedriver_path=str(tmp_path / "missing-chromedriver"), chrome_binary=CHROME))
    eng.http = FakeHttp()
    results = {}
    with pytest.raises(BrowserError):
        eng.run(TASK_BY_ID["kin"], [(0, "https://kin.naver.com/a"), (1, "https://kin.naver.com/b")],
                threading.Event(), on_result=lambda k, r: results.__setitem__(k, r))
    assert list(results) == [0] and results[0].status == "실패"
