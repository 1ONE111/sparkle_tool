// 추첨 도우미 - 핵심 로직 (raw 읽기, 게시글 확인, 규칙, 엑셀, 당첨 이력)
// C# 5 (.NET Framework 4.x 내장 csc)로 빌드한다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;

namespace Raffle {

 // ───────────────────────── 설정 (exe 옆 설정.ini)
 public class Settings {
  public string LinkPrefix = DefaultLink;
  public const string DefaultLink = "https://www.ktmmobile.com/event/eventDetail.do?ntcartSeq=";
  public int QualityChars = 500, SpecialViews = 300, MinViews = 10, MinMembers = 30000, BlackMonths = 3, CopyPercent = 90;
  public string ExcludeSites = DefaultExclude, ManualSites = "ppomppu.co.kr|뽐뿌";
  public const string DefaultExclude = "cafe.daum.net|다음카페;cashdoc|캐시닥;cashwalk|캐시워크;blog.naver.com|개인 채널(블로그);tistory.com|개인 채널(블로그);instagram.com|개인 채널(인스타그램);x.com|개인 채널(X);twitter.com|개인 채널(X);facebook.com|개인 채널(페이스북);youtube.com|개인 채널(유튜브);threads.net|개인 채널(스레드)";
  public string SaveFolder = "", FilePrefix = "", SpecialPrize = "", FirstPrize = "", CommentUrl = "";
  public int SpecialCount = 10, FirstCount = 0;
  public string Path = "";

  public static Settings Load(string path) {
   var s = new Settings(); s.Path = path;
   if (!File.Exists(path)) return s;
   foreach (var line in File.ReadAllLines(path, Encoding.UTF8)) {
    int i = line.IndexOf('='); if (i <= 0) continue;
    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1);
    switch (k) {
     case "LinkPrefix": s.LinkPrefix = v; break;
     case "QualityChars": s.QualityChars = Num(v, s.QualityChars); break;
     case "SpecialViews": s.SpecialViews = Num(v, s.SpecialViews); break;
     case "MinViews": s.MinViews = Num(v, s.MinViews); break;
     case "MinMembers": s.MinMembers = Num(v, s.MinMembers); break;
     case "BlackMonths": s.BlackMonths = Num(v, s.BlackMonths); break;
     case "CopyPercent": s.CopyPercent = Num(v, s.CopyPercent); break;
     case "ExcludeSites": s.ExcludeSites = v; break;
     case "ManualSites": s.ManualSites = v; break;
     case "SaveFolder": s.SaveFolder = v; break;
     case "FilePrefix": s.FilePrefix = v; break;
     case "SpecialPrize": s.SpecialPrize = v; break;
     case "FirstPrize": s.FirstPrize = v; break;
     case "CommentUrl": s.CommentUrl = v; break;
     case "SpecialCount": s.SpecialCount = Num(v, s.SpecialCount); break;
     case "FirstCount": s.FirstCount = Num(v, s.FirstCount); break;
    }
   }
   if (String.IsNullOrWhiteSpace(s.LinkPrefix)) s.LinkPrefix = DefaultLink;
   return s;
  }
  static int Num(string v, int d) { int n; return Int32.TryParse((v ?? "").Replace(",", "").Trim(), out n) ? n : d; }
  public void Save() {
   var sb = new StringBuilder();
   sb.AppendLine("LinkPrefix=" + LinkPrefix); sb.AppendLine("QualityChars=" + QualityChars); sb.AppendLine("SpecialViews=" + SpecialViews);
   sb.AppendLine("MinViews=" + MinViews); sb.AppendLine("MinMembers=" + MinMembers); sb.AppendLine("BlackMonths=" + BlackMonths);
   sb.AppendLine("CopyPercent=" + CopyPercent); sb.AppendLine("ExcludeSites=" + ExcludeSites); sb.AppendLine("ManualSites=" + ManualSites);
   sb.AppendLine("SaveFolder=" + SaveFolder); sb.AppendLine("FilePrefix=" + FilePrefix); sb.AppendLine("SpecialPrize=" + SpecialPrize);
   sb.AppendLine("FirstPrize=" + FirstPrize); sb.AppendLine("CommentUrl=" + CommentUrl); sb.AppendLine("SpecialCount=" + SpecialCount);
   sb.AppendLine("FirstCount=" + FirstCount);
   try { File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true)); } catch { }
  }
  public static List<KeyValuePair<string, string>> Sites(string list) {
   var r = new List<KeyValuePair<string, string>>();
   foreach (var part in (list ?? "").Split(';', '\n')) {
    var t = part.Trim(); if (t == "") continue;
    int i = t.IndexOf('|');
    string pat = (i < 0 ? t : t.Substring(0, i)).Trim().ToLowerInvariant(), name = i < 0 ? pat : t.Substring(i + 1).Trim();
    if (pat != "") r.Add(new KeyValuePair<string, string>(pat, name));
   }
   return r;
  }
  public static string MatchSite(List<KeyValuePair<string, string>> sites, string host) {
   host = (host ?? "").ToLowerInvariant();
   foreach (var s in sites) {
    string p = s.Key;
    if (p.Contains(".") ? (host == p || host.EndsWith("." + p)) : host.Contains(p)) return s.Value;
   }
   return null;
  }
  public string MembersText() { return MinMembers % 10000 == 0 ? (MinMembers / 10000) + "만" : MinMembers.ToString("#,##0"); }
 }

 // ───────────────────────── 데이터
 public enum PState { Ok, Excluded, Review }
 public enum RKind { None, Manual, Copy }
 public class Post {
  public Participant P; public int Seq;
  public string Url = "", FinalUrl = "", Community = "", CommKey = "", ArticleKey = "", Site = "";
  public int Views = -1; public long Members = -1; public DateTime? Written;
  public string Norm = "", Exact = ""; public int TextLen; public bool HasLink, HasImage, HasTable, Analyzed; public string Quality = "", Excerpt = ""; public List<string> Links = new List<string>();
  public PState BaseState = PState.Ok; public string BaseReason = ""; public RKind BaseKind = RKind.None; public string ManualWhy = "";
  // Analyze가 매번 다시 채우는 값
  public PState State; public string Reason = ""; public RKind Kind; public Post CopyOf; public int CopyPct;
  public int EffViews = -1; public string EffQuality = ""; public bool Decided;
  public HashSet<int> Grams;
  public string Key { get { return ArticleKey != "" ? ArticleKey : (FinalUrl != "" ? FinalUrl : Url); } }
 }
 public class Participant {
  public int No, Row; public string Time = "", Id = "", Nick = "";
  public List<Post> Posts = new List<Post>();
  public bool CommentMatched; public string CommentHint = "";
  public string Section = "", Reason = "", Note = ""; public int Sum; public bool CommentPending, Pending; public string BlackInfo = "";
  public string ExportSection = "";
 }
 public class Decision { public bool Accept; public int Views = -1; public string Quality = ""; }

 // ───────────────────────── xlsx 읽기 (Excel 없이 직접)
 public class XCell { public string Text = ""; public int Style; }
 public class XSheetData { public string Name = ""; public SortedDictionary<int, SortedDictionary<int, XCell>> Rows = new SortedDictionary<int, SortedDictionary<int, XCell>>(); public string Cell(int r, int c) { SortedDictionary<int, XCell> row; XCell x; return Rows.TryGetValue(r, out row) && row.TryGetValue(c, out x) ? x.Text : ""; } public int StyleOf(int r, int c) { SortedDictionary<int, XCell> row; XCell x; return Rows.TryGetValue(r, out row) && row.TryGetValue(c, out x) ? x.Style : 0; } }
 public class XBook {
  public List<XSheetData> Sheets = new List<XSheetData>(); public List<string> XfFill = new List<string>();
  const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  public static XBook Read(string path) {
   byte[] bytes;
   using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { var ms = new MemoryStream(); fs.CopyTo(ms); bytes = ms.ToArray(); }
   var book = new XBook();
   using (var z = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read)) {
    var shared = new List<string>();
    var sst = Doc(z, "xl/sharedStrings.xml");
    if (sst != null) foreach (XmlNode si in sst.GetElementsByTagName("si")) { var sb = new StringBuilder(); foreach (XmlNode t in ((XmlElement)si).GetElementsByTagName("t")) sb.Append(t.InnerText); shared.Add(sb.ToString()); }
    var dateXf = new HashSet<int>();
    var st = Doc(z, "xl/styles.xml");
    if (st != null) {
     var custom = new Dictionary<int, string>();
     foreach (XmlElement nf in st.GetElementsByTagName("numFmt")) custom[Int(nf.GetAttribute("numFmtId"))] = nf.GetAttribute("formatCode");
     var fills = new List<string>();
     foreach (XmlElement f in st.GetElementsByTagName("fill")) {
      string rgb = ""; var fg = f.GetElementsByTagName("fgColor");
      if (fg.Count > 0) { var e = (XmlElement)fg[0]; rgb = e.GetAttribute("rgb"); if (rgb == "" && e.GetAttribute("indexed") == "13") rgb = "FFFFFF00"; }
      fills.Add(rgb.ToUpperInvariant());
     }
     var xfsNodes = st.GetElementsByTagName("cellXfs");
     if (xfsNodes.Count > 0) {
      int i = 0;
      foreach (XmlNode n in xfsNodes[0].ChildNodes) {
       var xf = n as XmlElement; if (xf == null || xf.LocalName != "xf") continue;
       int fmt = Int(xf.GetAttribute("numFmtId")), fill = Int(xf.GetAttribute("fillId"));
       book.XfFill.Add(fill >= 0 && fill < fills.Count ? fills[fill] : "");
       string code; bool isDate = (fmt >= 14 && fmt <= 22) || (fmt >= 45 && fmt <= 47) || (custom.TryGetValue(fmt, out code) && Regex.IsMatch(Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]", ""), "[ymdhs]", RegexOptions.IgnoreCase));
       if (isDate) dateXf.Add(i);
       i++;
      }
     }
    }
    var wb = Doc(z, "xl/workbook.xml"); var rels = Doc(z, "xl/_rels/workbook.xml.rels");
    var target = new Dictionary<string, string>();
    if (rels != null) foreach (XmlElement r in rels.GetElementsByTagName("Relationship")) target[r.GetAttribute("Id")] = r.GetAttribute("Target");
    foreach (XmlElement sh in wb.GetElementsByTagName("sheet")) {
     string rid = sh.GetAttribute("id", NsR), tg;
     if (!target.TryGetValue(rid, out tg)) continue;
     string p = tg.StartsWith("/") ? tg.Substring(1) : "xl/" + tg;
     var d = Doc(z, p); if (d == null) continue;
     var data = new XSheetData { Name = sh.GetAttribute("name") };
     foreach (XmlElement c in d.GetElementsByTagName("c")) {
      string rf = c.GetAttribute("r"); int col, row; if (!Ref(rf, out row, out col)) continue;
      string t = c.GetAttribute("t"); int s = Int(c.GetAttribute("s")); if (s < 0) s = 0;
      string v = ""; var vn = c.GetElementsByTagName("v"); if (vn.Count > 0) v = vn[0].InnerText;
      string text;
      if (t == "s") { int k = Int(v); text = k >= 0 && k < shared.Count ? shared[k] : ""; }
      else if (t == "inlineStr") { var sb = new StringBuilder(); foreach (XmlNode tn in c.GetElementsByTagName("t")) sb.Append(tn.InnerText); text = sb.ToString(); }
      else if (t == "str" || t == "b" || t == "e") text = v;
      else {
       double num; text = v;
       if (v != "" && Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) {
        if (dateXf.Contains(s)) { try { text = DateTime.FromOADate(num).ToString(num % 1 == 0 ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss"); } catch { } }
        else text = num % 1 == 0 && Math.Abs(num) < 1e15 ? ((long)num).ToString(CultureInfo.InvariantCulture) : num.ToString(CultureInfo.InvariantCulture);
       }
      }
      SortedDictionary<int, XCell> rowMap; if (!data.Rows.TryGetValue(row, out rowMap)) { rowMap = new SortedDictionary<int, XCell>(); data.Rows[row] = rowMap; }
      rowMap[col] = new XCell { Text = text ?? "", Style = s };
     }
     book.Sheets.Add(data);
    }
   }
   return book;
  }
  static XmlDocument Doc(ZipArchive z, string name) {
   var e = z.GetEntry(name); if (e == null) return null;
   var d = new XmlDocument { XmlResolver = null }; using (var s = e.Open()) d.Load(s); return d;
  }
  static int Int(string s) { int n; return Int32.TryParse(s, out n) ? n : -1; }
  public static bool Ref(string r, out int row, out int col) {
   row = 0; col = 0; int i = 0;
   while (i < r.Length && Char.IsLetter(r[i])) { col = col * 26 + (Char.ToUpperInvariant(r[i]) - 'A' + 1); i++; }
   return i > 0 && Int32.TryParse(r.Substring(i), out row);
  }
  public bool IsYellow(int style) { if (style < 0 || style >= XfFill.Count) return false; var f = XfFill[style]; return f == "FFFFFF00" || f == "FFFF00"; }
 }

 // ───────────────────────── 네이버폼 raw 읽기
 public static class RawReader {
  public static List<Participant> Read(string path, out string error) {
   error = ""; var list = new List<Participant>();
   XBook book;
   try { book = XBook.Read(path); } catch (Exception ex) { error = "파일을 열지 못했어요. " + ex.Message; return list; }
   if (book.Sheets.Count == 0) { error = "시트가 없어요."; return list; }
   var sh = book.Sheets[0];
   if (sh.Rows.Count == 0) { error = "내용이 없어요."; return list; }
   int headRow = sh.Rows.Keys.First();
   int cTime = -1, cNo = -1, cId = -1, cNick = -1; var cUrls = new List<int>();
   foreach (var kv in sh.Rows[headRow]) {
    string h = kv.Value.Text.Replace("\n", " ");
    if (h.Contains("응답일시")) cTime = kv.Key;
    else if (h.Trim() == "참여자" || h.StartsWith("참여자")) cNo = kv.Key;
    else if (h.Contains("아이디")) cId = kv.Key;
    else if (h.Contains("닉네임")) cNick = kv.Key;
    else if (!h.Contains("인증") && (h.Contains("게시글 링크") || h.Contains("게시글 URL") || h.Contains("URL"))) cUrls.Add(kv.Key);
   }
   if (cId < 0 && cNick < 0) { error = "네이버 아이디·닉네임 열을 찾지 못했어요. 네이버폼 raw 파일이 맞는지 확인해 주세요."; return list; }
   if (cUrls.Count == 0) { error = "게시글 링크 열을 찾지 못했어요."; return list; }
   int seq = 0;
   foreach (var row in sh.Rows.Keys) {
    if (row == headRow) continue;
    var p = new Participant { Row = row };
    p.Id = sh.Cell(row, cId).Trim(); p.Nick = sh.Cell(row, cNick).Trim(); p.Time = cTime >= 0 ? sh.Cell(row, cTime).Trim() : "";
    if (p.Id == "" && p.Nick == "") continue;
    seq++; int no; p.No = cNo >= 0 && Int32.TryParse(sh.Cell(row, cNo).Trim(), out no) ? no : seq;
    int k = 0;
    foreach (int c in cUrls) {
     string u = CleanUrl(sh.Cell(row, c)); if (u == "") continue;
     p.Posts.Add(new Post { P = p, Seq = ++k, Url = u });
    }
    list.Add(p);
   }
   list.Sort((a, b) => a.No.CompareTo(b.No));
   return list;
  }
  public static string CleanUrl(string s) {
   s = (s ?? "").Trim(); if (s == "") return "";
   var m = Regex.Match(s, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase);
   if (m.Success) return m.Value.TrimEnd('.', ',', ')', ']');
   m = Regex.Match(s, @"([a-z0-9\-]+\.)+[a-z]{2,}(/[^\s<>""']*)?", RegexOptions.IgnoreCase);
   return m.Success ? "https://" + m.Value : "";
  }
  public static string EventName(string path) {
   string n = System.IO.Path.GetFileNameWithoutExtension(path) ?? "";
   n = Regex.Replace(n, @"[_\s]*raw[\s_]*data.*$", "", RegexOptions.IgnoreCase);
   n = Regex.Replace(n, @"[_\s]*raw$", "", RegexOptions.IgnoreCase);
   n = Regex.Replace(n, @"\s*\(\d+\)$", "");
   return n.Trim(' ', '_');
  }
 }

 // ───────────────────────── 인터넷 (게시글·댓글 읽기)
 public static class Net {
  public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";
  public const string MUA = "Mozilla/5.0 (Linux; Android 13; SM-S918N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Mobile Safari/537.36";
  static Net() { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; ServicePointManager.DefaultConnectionLimit = 8; }
  public static string Get(string url, string ua, string referer, out int status) {
   status = 0;
   try {
    var req = (HttpWebRequest)WebRequest.Create(url);
    req.UserAgent = ua ?? UA; req.Timeout = 20000; req.ReadWriteTimeout = 20000; req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
    req.Accept = "text/html,application/json,*/*"; req.Headers["Accept-Language"] = "ko-KR,ko;q=0.9";
    if (referer != null) req.Referer = referer;
    using (var resp = (HttpWebResponse)req.GetResponse()) { status = (int)resp.StatusCode; return Read(resp); }
   } catch (WebException ex) {
    var r = ex.Response as HttpWebResponse;
    if (r != null) { status = (int)r.StatusCode; try { return Read(r); } catch { return ""; } finally { r.Close(); } }
    status = -1; return "";
   } catch { status = -1; return ""; }
  }
  static string Read(HttpWebResponse resp) {
   using (var s = resp.GetResponseStream()) {
    var ms = new MemoryStream(); s.CopyTo(ms); var b = ms.ToArray();
    string cs = (resp.CharacterSet ?? "").ToLowerInvariant();
    Encoding enc = Encoding.UTF8;
    if (cs.Contains("euc-kr") || cs.Contains("ks_c") || cs.Contains("949")) enc = Encoding.GetEncoding(949);
    return enc.GetString(b);
   }
  }
  // naver.me 같은 짧은 주소를 원래 주소로
  public static string Resolve(string url) {
   string cur = url;
   for (int i = 0; i < 5; i++) {
    try {
     var req = (HttpWebRequest)WebRequest.Create(cur); req.AllowAutoRedirect = false; req.UserAgent = UA; req.Timeout = 15000; req.Method = "GET";
     using (var resp = (HttpWebResponse)req.GetResponse()) {
      int code = (int)resp.StatusCode; string loc = resp.Headers["Location"];
      if (code >= 300 && code < 400 && !String.IsNullOrEmpty(loc)) { cur = new Uri(new Uri(cur), loc).ToString(); continue; }
      return cur;
     }
    } catch (WebException ex) {
     var r = ex.Response as HttpWebResponse;
     if (r != null) { int code = (int)r.StatusCode; string loc = r.Headers["Location"]; r.Close(); if (code >= 300 && code < 400 && !String.IsNullOrEmpty(loc)) { cur = new Uri(new Uri(cur), loc).ToString(); continue; } }
     return cur;
    } catch { return cur; }
   }
   return cur;
  }
  public static object Json(string s) {
   if (String.IsNullOrEmpty(s)) return null;
   try { var js = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 200 }; return js.DeserializeObject(s); } catch { return null; }
  }
  public static object J(object o, params string[] path) {
   foreach (var k in path) { var d = o as Dictionary<string, object>; if (d == null || !d.TryGetValue(k, out o)) return null; }
   return o;
  }
  public static long L(object o) { if (o == null) return -1; try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return -1; } }
  public static string S(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture); }
 }

 // ───────────────────────── 네이버 카페
 public class CafeRef { public string Club = ""; public long ClubId = -1; public long ArticleId = -1; }
 public static class Cafe {
  static readonly Dictionary<string, long> ClubIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
  public static CafeRef Parse(string url) {
   Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
   string host = u.Host.ToLowerInvariant(); if (!host.EndsWith("cafe.naver.com")) return null;
   string path = u.AbsolutePath, q = Uri.UnescapeDataString(u.Query ?? "");
   var r = new CafeRef(); Match m;
   if ((m = Regex.Match(path, @"/ca-fe/(?:web/)?cafes/([^/]+)/articles/(\d+)", RegexOptions.IgnoreCase)).Success) {
    string c = m.Groups[1].Value; long id;
    bool useId = !Regex.IsMatch(q, "useCafeId=false", RegexOptions.IgnoreCase);
    if (useId && Int64.TryParse(c, out id)) r.ClubId = id; else r.Club = c;
    r.ArticleId = Int64.Parse(m.Groups[2].Value); return r;
   }
   var cid = Regex.Match(q, @"clubid=(\d+)", RegexOptions.IgnoreCase); var aid = Regex.Match(q, @"articleid=(\d+)", RegexOptions.IgnoreCase);
   if (aid.Success) {
    r.ArticleId = Int64.Parse(aid.Groups[1].Value);
    if (cid.Success) r.ClubId = Int64.Parse(cid.Groups[1].Value);
    else { var seg = path.Trim('/').Split('/'); if (seg.Length > 0 && seg[0] != "" && !seg[0].Contains(".")) r.Club = seg[0]; }
    return r.ClubId > 0 || r.Club != "" ? r : null;
   }
   if ((m = Regex.Match(path, @"^/([A-Za-z0-9_\-]+)/(\d+)", RegexOptions.IgnoreCase)).Success) { r.Club = m.Groups[1].Value; r.ArticleId = Int64.Parse(m.Groups[2].Value); return r; }
   return null;
  }
  public static long ClubId(CafeRef r) {
   if (r.ClubId > 0) return r.ClubId;
   long id;
   lock (ClubIds) if (ClubIds.TryGetValue(r.Club, out id)) return id;
   int st; string html = Net.Get("https://cafe.naver.com/" + r.Club, Net.UA, "https://cafe.naver.com/", out st);
   var m = Regex.Match(html ?? "", @"(?:g_sClubId\s*=\s*""|clubid=|""clubId""\s*:\s*""?)(\d+)", RegexOptions.IgnoreCase);
   id = m.Success ? Int64.Parse(m.Groups[1].Value) : -1;
   if (id < 0) {
    var j = Net.Json(Net.Get("https://apis.naver.com/cafe-web/cafe2/CafeGateInfo.json?cluburl=" + Uri.EscapeDataString(r.Club), Net.UA, "https://cafe.naver.com/", out st));
    id = Net.L(Net.J(j, "message", "result", "cafeInfoView", "cafeId"));
   }
   lock (ClubIds) ClubIds[r.Club] = id;
   return id;
  }
  public static object Article(long clubId, long articleId, out int status) {
   string s = Net.Get("https://apis.naver.com/cafe-web/cafe-articleapi/v2.1/cafes/" + clubId + "/articles/" + articleId + "?useCafeId=true", Net.UA, "https://cafe.naver.com/", out status);
   return Net.Json(s) ?? (object)s;
  }
  // 이벤트 글 댓글 작성자 닉네임 전부
  public static List<string> CommentNicks(string url, out string error) {
   error = ""; var nicks = new List<string>();
   var r = Parse(Net.Resolve(url));
   if (r == null) { error = "공식카페 글 주소를 읽지 못했어요."; return nicks; }
   long club = ClubId(r); if (club <= 0) { error = "카페를 찾지 못했어요."; return nicks; }
   var seen = new HashSet<string>();
   for (int page = 1; page <= 100; page++) {
    int st; var j = Net.Json(Net.Get("https://apis.naver.com/cafe-web/cafe-articleapi/v2/cafes/" + club + "/articles/" + r.ArticleId + "/comments/pages/" + page + "?requestFrom=A&orderBy=asc", Net.UA, "https://cafe.naver.com/", out st));
    var items = Net.J(j, "result", "comments", "items") as object[];
    if (items == null) { if (page == 1) error = "댓글을 읽지 못했어요 (" + st + ")."; break; }
    int added = 0;
    foreach (var it in items) {
     string cid = Net.S(Net.J(it, "id")); if (cid != "" && !seen.Add(cid)) continue;
     added++;
     var del = Net.J(it, "isDeleted"); if (del is bool && (bool)del) continue;
     string nick = Net.S(Net.J(it, "writer", "nick")).Trim(); if (nick != "") nicks.Add(nick);
    }
    var next = Net.J(j, "result", "hasNext");
    if (added == 0 || !(next is bool) || !(bool)next) break;
    Thread.Sleep(200);
   }
   return nicks;
  }
 }

 // ───────────────────────── 본문 분석 (글자수·링크·이미지·복붙 비교용 글)
 public static class Content {
  static string NormLink(string s) { s = WebUtility.HtmlDecode(s ?? "").Trim().ToLowerInvariant(); s = Regex.Replace(s, @"^https?://", ""); if (s.StartsWith("www.")) s = s.Substring(4); return s; }
  public static void Analyze(Post p, string html, Settings set) {
   html = html ?? "";
   string decoded = WebUtility.HtmlDecode(html);
   string want = NormLink(set.LinkPrefix);
   p.Links = new List<string>();
   foreach (Match m in Regex.Matches(decoded, @"(?:https?://)?(?:www\.)?[a-z0-9\-]+(?:\.[a-z0-9\-]+)+/[^\s""'<>\\]*", RegexOptions.IgnoreCase)) { string l = NormLink(m.Value); if (!p.Links.Contains(l)) p.Links.Add(l); }
   p.HasLink = HasLink(p.Links, set.LinkPrefix); p.Analyzed = true;
   if (html.Contains("se-component")) p.HasImage = Regex.IsMatch(html, @"se-component\s+se-(image|imageStrip|imageGroup|gallery)\b|se-module-image", RegexOptions.IgnoreCase);
   else {
    p.HasImage = false;
    foreach (Match m in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase)) { string tag = m.Value.ToLowerInvariant(); if (tag.Contains("sticker") || tag.Contains("emoticon") || tag.Contains("dccon") || tag.Contains("dthumb") || tag.Contains("icon")) continue; p.HasImage = true; break; }
   }
   p.HasTable = Regex.IsMatch(html, @"<table\b|se-component\s+se-table\b", RegexOptions.IgnoreCase);
   string body;
   var paras = Regex.Matches(html, @"<p[^>]*class=""[^""]*se-text-paragraph[^""]*""[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
   if (paras.Count > 0) { var sb = new StringBuilder(); foreach (Match m in paras) sb.Append(m.Groups[1].Value).Append('\n'); body = sb.ToString(); }
   else body = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
   body = Regex.Replace(body, @"<br\s*/?>|</p>|</div>", "\n", RegexOptions.IgnoreCase);
   body = WebUtility.HtmlDecode(Regex.Replace(body, @"<[^>]+>", " "));
   body = Regex.Replace(body, @"https?://\S+", " ");
   body = body.Replace('​', ' ').Replace(' ', ' ');
   string plain = Regex.Replace(body, @"[ \t]+", " ").Trim();
   p.Excerpt = Regex.Replace(plain, @"\s*\n\s*", " ").Trim(); if (p.Excerpt.Length > 400) p.Excerpt = p.Excerpt.Substring(0, 400) + "…";
   p.TextLen = Regex.Replace(plain, @"\s", "").Length;
   p.Norm = Regex.Replace(plain.ToLowerInvariant(), @"[\s\p{P}\p{S}]", "");
   p.Exact = Regex.Replace(plain, @"\s", "");
   p.Quality = Quality(p.HasLink, p.HasImage, p.HasTable);
  }
  public static bool HasLink(List<string> links, string prefix) { string want = NormLink(prefix); if (want == "" || links == null) return false; foreach (var l in links) if (l.StartsWith(want)) return true; return false; }
  // 상: 링크 + 이미지, 또는 링크 + 표 / 중: 이미지·표·링크 중 하나 / 하: 글만
  public static string Quality(bool link, bool img, bool table) {
   if (link && (img || table)) return "상";
   if (link || img || table) return "중";
   return "하";
  }
  public static HashSet<int> Grams(string s) {
   var h = new HashSet<int>(); for (int i = 0; i + 2 < s.Length; i++) h.Add((s[i] * 31 + s[i + 1]) * 31 + s[i + 2]); return h;
  }
  public static int Similarity(HashSet<int> a, HashSet<int> b) {
   if (a.Count == 0 || b.Count == 0) return 0;
   int inter = 0; var small = a.Count < b.Count ? a : b; var big = small == a ? b : a;
   foreach (var g in small) if (big.Contains(g)) inter++;
   return (int)Math.Round(200.0 * inter / (a.Count + b.Count));
  }
 }

 // ───────────────────────── 게시글 하나씩 확인
 public static class Fetcher {
  public static void Fetch(Post p, Settings set) {
   p.BaseState = PState.Ok; p.BaseReason = ""; p.BaseKind = RKind.None; p.ManualWhy = ""; p.Views = -1; p.Members = -1; p.Written = null;
   p.Quality = ""; p.HasLink = false; p.HasImage = false; p.HasTable = false; p.Analyzed = false; p.Links = new List<string>(); p.TextLen = 0; p.Norm = ""; p.Exact = ""; p.Excerpt = ""; p.Grams = null;
   string url = p.Url;
   Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) { Manual(p, "주소를 읽을 수 없어요", "확인 필요"); return; }
   string host = u.Host.ToLowerInvariant();
   if (host == "naver.me" || host.EndsWith(".naver.me") || host == "me2.do" || host == "bit.ly") { url = Net.Resolve(url); Uri.TryCreate(url, UriKind.Absolute, out u); host = u == null ? "" : u.Host.ToLowerInvariant(); }
   p.FinalUrl = url;
   string ex = Settings.MatchSite(Settings.Sites(set.ExcludeSites), host);
   if (ex != null) { p.Community = ex.StartsWith("개인 채널") ? ex.Replace("개인 채널", "").Trim('(', ')', ' ') : ex; p.CommKey = host; p.ArticleKey = Norm(url); Exclude(p, ex); return; }
   string man = Settings.MatchSite(Settings.Sites(set.ManualSites), host);
   if (man != null) { p.Community = man; p.CommKey = "site:" + man; p.ArticleKey = SiteKey(u); Manual(p, man + " · 자동으로 읽히지 않아요", man); return; }
   if (host.EndsWith("cafe.naver.com")) { FetchCafe(p, set); return; }
   if (host.EndsWith("dcinside.com")) { FetchDc(p, u, set); return; }
   p.Community = host; p.CommKey = "site:" + host; p.ArticleKey = Norm(url);
   Manual(p, "자동으로 읽을 수 없는 사이트예요", host);
  }
  static void Exclude(Post p, string why) { p.BaseState = PState.Excluded; p.BaseReason = why; }
  static void Manual(Post p, string why, string community) { p.BaseState = PState.Review; p.BaseKind = RKind.Manual; p.ManualWhy = why; if (p.Community == "") p.Community = community; }
  static string Norm(string url) { var s = url.ToLowerInvariant(); s = Regex.Replace(s, @"^https?://(www\.|m\.)?", ""); s = Regex.Replace(s, @"[?#].*$", ""); return s.TrimEnd('/'); }
  static string SiteKey(Uri u) {
   string q = u.Query ?? ""; var id = Regex.Match(q, @"[?&]id=([^&]+)"); var no = Regex.Match(q, @"[?&]no=(\d+)");
   if (id.Success && no.Success) return u.Host.Replace("www.", "").Replace("m.", "") + ":" + id.Groups[1].Value + ":" + no.Groups[1].Value;
   return Norm(u.ToString());
  }
  static void FetchCafe(Post p, Settings set) {
   var r = Cafe.Parse(p.FinalUrl);
   if (r == null) { p.Community = "네이버 카페"; p.CommKey = "cafe:?"; p.ArticleKey = Norm(p.FinalUrl); Manual(p, "카페 글 주소가 아니에요", "네이버 카페"); return; }
   long club = Cafe.ClubId(r);
   p.CommKey = club > 0 ? "cafe:" + club : "cafe:" + r.Club.ToLowerInvariant();
   p.ArticleKey = p.CommKey + ":" + r.ArticleId;
   p.Community = r.Club != "" ? r.Club : "네이버 카페";
   if (club <= 0) { Manual(p, "카페를 찾지 못했어요", p.Community); return; }
   int st = 0; object j = null;
   for (int t = 0; t < 2; t++) { j = Cafe.Article(club, r.ArticleId, out st); if (st == 200 || (st >= 400 && st < 500)) break; Thread.Sleep(800); }
   var art = Net.J(j, "result", "article");
   string cafeName = Net.S(Net.J(j, "result", "cafe", "name"));
   if (cafeName != "") p.Community = CleanName(cafeName);
   p.Members = Net.L(Net.J(j, "result", "cafe", "memberCount"));
   if (art == null) {
    string reason = Net.S(Net.J(j, "result", "reason")), code = Net.S(Net.J(j, "result", "errorCode"));
    if (reason == "" && j is string) reason = (string)j;
    string moreName = Net.S(Net.J(j, "result", "more", "cafeName")); if (moreName != "") p.Community = CleanName(moreName);
    long mc = Net.L(Net.J(j, "result", "more", "cafeMemberCount")); if (mc >= 0) p.Members = mc;
    if (st == 404 || Regex.IsMatch(reason, "삭제|존재하지|not.?exist|not.?found|deleted", RegexOptions.IgnoreCase)) { Exclude(p, "삭제된 글"); return; }
    if (st == 401 || st == 403 || Regex.IsMatch(reason, "로그인|멤버|회원|가입|권한|등급", RegexOptions.IgnoreCase)) { Exclude(p, "전체 공개가 아닌 글"); return; }
    Manual(p, "자동 확인 실패 (" + st + (code != "" ? " " + code : "") + ")", p.Community); return;
   }
   p.Views = (int)Math.Max(-1, Net.L(Net.J(art, "readCount")));
   long wd = Net.L(Net.J(art, "writeDate")); if (wd > 0) p.Written = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(wd).ToLocalTime();
   string html = Net.S(Net.J(art, "contentHtml"));
   if (html == "") { Manual(p, "본문을 읽지 못했어요 (멤버공개일 수 있어요)", p.Community); return; }
   Content.Analyze(p, html, set);
  }
  public static string CleanName(string s) {
   s = Regex.Replace(s ?? "", @"[^\p{L}\p{N}\s&·\.\-\+]", " ");
   s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '·');
   int dash = s.IndexOf('-'); if (dash >= 2) s = s.Substring(0, dash).Trim();
   if (s.Length > 15 && s.Contains(" ")) s = s.Substring(0, s.IndexOf(' '));
   return s;
  }
  static void FetchDc(Post p, Uri u, Settings set) {
   p.Community = "디시인사이드"; p.CommKey = "site:dcinside";
   string gall = "", no = ""; Match m;
   string q = u.Query ?? "";
   if ((m = Regex.Match(u.AbsolutePath, @"/board/([^/]+)/(\d+)")).Success) { gall = m.Groups[1].Value; no = m.Groups[2].Value; }
   else { var a = Regex.Match(q, @"[?&]id=([^&]+)"); var b = Regex.Match(q, @"[?&]no=(\d+)"); if (a.Success && b.Success) { gall = a.Groups[1].Value; no = b.Groups[1].Value; } }
   if (gall == "") { p.ArticleKey = Norm(p.FinalUrl); Manual(p, "디시 글 주소를 읽지 못했어요", "디시인사이드"); return; }
   p.ArticleKey = "dc:" + gall + ":" + no;
   int st; string html = Net.Get("https://m.dcinside.com/board/" + gall + "/" + no, Net.MUA, "https://m.dcinside.com/", out st);
   var vm = Regex.Match(html ?? "", @"조회수\s*(?:<[^>]+>\s*)*([\d,]+)");
   if (st != 200 || !vm.Success) {
    if (Regex.IsMatch(html ?? "", "삭제된 게시물|존재하지 않는")) { Exclude(p, "삭제된 글"); return; }
    Manual(p, "디시 글을 읽지 못했어요 (" + st + ")", "디시인사이드"); return;
   }
   p.Views = Int32.Parse(vm.Groups[1].Value.Replace(",", ""));
   var dm = Regex.Match(html, @"(\d{4})[.\-](\d{2})[.\-](\d{2})\s+(\d{2}):(\d{2})");
   if (dm.Success) { try { p.Written = new DateTime(Int32.Parse(dm.Groups[1].Value), Int32.Parse(dm.Groups[2].Value), Int32.Parse(dm.Groups[3].Value), Int32.Parse(dm.Groups[4].Value), Int32.Parse(dm.Groups[5].Value), 0); } catch { } }
   int s = html.IndexOf("thum-txtin", StringComparison.Ordinal);
   string body = s >= 0 ? html.Substring(s, Math.Min(80000, html.Length - s)) : "";
   body = Regex.Replace(body, @"<script\b.*?</script>|<ins\b.*?</ins>|<style\b.*?</style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
   int e = -1; foreach (var stop in new[] { "class=\"reco-search", "class=\"reco-area", "id=\"recommend_join", "class=\"btn-justify-area", "id=\"comment_box", "class=\"all-comment" }) { int k = body.IndexOf(stop, StringComparison.Ordinal); if (k > 0 && (e < 0 || k < e)) e = k; }
   if (e > 0) body = body.Substring(0, e);
   Content.Analyze(p, "<div " + body, set);
  }
 }

 // ───────────────────────── 당첨 이력 (exe 옆 당첨이력.csv)
 public class HEntry { public string Ym = "", Round = "", Kind = "", Nick = "", Id = ""; public DateTime Date; }
 public class History {
  public List<HEntry> Items = new List<HEntry>(); public string Path = "";
  public static History Load(string path) {
   var h = new History { Path = path };
   if (!File.Exists(path)) return h;
   bool first = true;
   foreach (var line in File.ReadAllLines(path, Encoding.UTF8)) {
    if (first) { first = false; if (line.StartsWith("연월")) continue; }
    var f = Csv(line); if (f.Count < 6) continue;
    DateTime d; if (!DateTime.TryParse(f[5], CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
    h.Items.Add(new HEntry { Ym = f[0], Round = f[1], Kind = f[2], Nick = f[3], Id = f[4], Date = d });
   }
   return h;
  }
  public void Save() {
   var sb = new StringBuilder("연월,회차,구분,닉네임,아이디,발표일\r\n");
   foreach (var e in Items.OrderBy(x => x.Ym).ThenBy(x => x.Date).ThenBy(x => x.Round))
    sb.Append(Q(e.Ym)).Append(',').Append(Q(e.Round)).Append(',').Append(Q(e.Kind)).Append(',').Append(Q(e.Nick)).Append(',').Append(Q(e.Id)).Append(',').Append(e.Date.ToString("yyyy-MM-dd")).Append("\r\n");
   File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
  }
  static string Q(string s) { s = s ?? ""; return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
  static List<string> Csv(string line) {
   var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
   for (int i = 0; i < line.Length; i++) {
    char c = line[i];
    if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
    else if (c == '"') q = true; else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c);
   }
   r.Add(sb.ToString()); return r;
  }
  public static string Key(string s) { return Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s", ""); }
  public static DateTime Until(HEntry e, int months) { return e.Date.Date.AddMonths(months); }
  public HEntry FindBlack(string id, string nick, int months, DateTime today) {
   string a = Key(id), b = Key(nick); HEntry best = null;
   foreach (var e in Items) {
    if (e.Kind != "선착순" || Until(e, months) <= today.Date) continue;
    string ei = Key(e.Id), en = Key(e.Nick);
    bool hit = (a != "" && (a == ei || a == en)) || (b != "" && (b == en || b == ei));
    if (hit && (best == null || e.Date > best.Date)) best = e;
   }
   return best;
  }
  public int BlackCount(int months, DateTime today) {
   var set = new HashSet<string>();
   foreach (var e in Items) if (e.Kind == "선착순" && Until(e, months) > today.Date) set.Add(Key(e.Id) != "" ? Key(e.Id) : Key(e.Nick));
   return set.Count;
  }
  public static string Label(HEntry e) {
   var m = Regex.Match(e.Ym ?? "", @"^(\d{4})-(\d{1,2})$");
   return (m.Success ? m.Groups[1].Value + "년 " + Int32.Parse(m.Groups[2].Value) + "월" : e.Ym) + (String.IsNullOrEmpty(e.Round) ? "" : " " + e.Round);
  }
 }

 // ───────────────────────── 규칙 적용
 public class Summary { public int Special, First, Excluded, Review, Responses, Posts; }
 public static class Engine {
  public static Summary Analyze(List<Participant> ps, Settings set, List<string> commentNicks, History hist, Dictionary<string, Decision> dec, DateTime today) {
   var all = ps.SelectMany(x => x.Posts).ToList();
   // 1) 기본 상태 + 직접 확인 결정
   foreach (var p in all) {
    p.State = p.BaseState; p.Reason = p.BaseReason; p.Kind = p.BaseKind; p.CopyOf = null; p.CopyPct = 0; p.EffViews = p.Views; p.Decided = false;
    if (p.Analyzed) { p.HasLink = Content.HasLink(p.Links, set.LinkPrefix); p.Quality = Content.Quality(p.HasLink, p.HasImage, p.HasTable); }
    p.EffQuality = p.Quality;
    if (p.State == PState.Ok && p.Members >= 0 && p.Members <= set.MinMembers) { p.State = PState.Excluded; p.Reason = "카페 가입자수 " + set.MembersText() + " 이하"; }
    if (p.State == PState.Ok && p.Views >= 0 && p.Views < set.MinViews) { p.State = PState.Excluded; p.Reason = "조회수 " + set.MinViews + " 미만"; }
    if (p.Kind == RKind.Manual) {
     Decision d;
     if (dec.TryGetValue("post:" + p.Key, out d)) {
      p.Decided = true;
      if (d.Accept) { p.State = PState.Ok; p.EffViews = d.Views; p.EffQuality = d.Quality; if (d.Views < set.MinViews) { p.State = PState.Excluded; p.Reason = "조회수 " + set.MinViews + " 미만"; } }
      else { p.State = PState.Excluded; p.Reason = "확인 불가"; }
     }
    }
   }
   // 2) 같은 게시글 중복 제출: 먼저 낸 사람만
   var firstByKey = new Dictionary<string, Post>();
   foreach (var pa in ps) foreach (var p in pa.Posts) {
    if (p.State == PState.Excluded || p.Key == "") continue;
    Post f; if (firstByKey.TryGetValue(p.Key, out f)) { p.State = PState.Excluded; p.Reason = "중복 게시글" + (f.P == pa ? "" : " (" + f.P.No + "번과 같은 글)"); }
    else firstByKey[p.Key] = p;
   }
   // 3) 복붙의심: 본문이 100% 같은 글만(띄어쓰기·줄바꿈 차이는 무시). 먼저 올라온 글은 그대로, 나중 글만 검토
   var cand = all.Where(p => p.State == PState.Ok && p.Exact.Length >= 10).OrderBy(p => p.Written ?? DateTime.MaxValue).ThenBy(p => p.P.No).ThenBy(p => p.Seq).ToList();
   var firstByText = new Dictionary<string, Post>();
   for (int j = 0; j < cand.Count; j++) {
    Post orig; if (!firstByText.TryGetValue(cand[j].Exact, out orig)) { firstByText[cand[j].Exact] = cand[j]; continue; }
    var p = cand[j]; p.CopyOf = orig; p.CopyPct = 100;
    Decision d;
    if (dec.TryGetValue("copy:" + p.Key, out d)) { p.Decided = true; if (!d.Accept) { p.State = PState.Excluded; p.Reason = "복붙의심"; } }
    else { p.State = PState.Review; p.Kind = RKind.Copy; }
   }
   // 4) 같은 커뮤니티 2건 이상: 조회수 많은 글 1건만
   foreach (var pa in ps) {
    foreach (var g in pa.Posts.Where(p => p.State != PState.Excluded && p.CommKey != "").GroupBy(p => p.CommKey)) {
     if (g.Count() < 2) continue;
     var keep = g.OrderByDescending(p => p.EffViews).ThenBy(p => p.Seq).First();
     foreach (var q in g) if (q != keep) { q.State = PState.Excluded; q.Reason = "커뮤니티 중복"; }
    }
   }
   // 5) 사람 단위
   var nickSet = new HashSet<string>(); var nickList = new List<string>();
   if (commentNicks != null) foreach (var n in commentNicks) { var k = History.Key(n); if (nickSet.Add(k)) nickList.Add(n); }
   var sum = new Summary { Responses = ps.Count, Posts = all.Count };
   foreach (var pa in ps) {
    pa.Note = ""; pa.Reason = ""; pa.BlackInfo = ""; pa.CommentPending = false; pa.Pending = false; pa.CommentMatched = true; pa.CommentHint = "";
    var valid = pa.Posts.Where(p => p.State == PState.Ok).ToList();
    pa.Sum = valid.Sum(p => Math.Max(0, p.EffViews));
    pa.Pending = pa.Posts.Any(p => p.State == PState.Review);
    bool commentRejected = false;
    if (commentNicks != null) {
     pa.CommentMatched = nickSet.Contains(History.Key(pa.Nick)) || nickSet.Contains(History.Key(pa.Id));
     if (!pa.CommentMatched) {
      pa.CommentHint = Closest(pa.Nick, nickList);
      Decision d;
      if (dec.TryGetValue("cmt:" + pa.No, out d)) { if (!d.Accept) commentRejected = true; }
      else pa.CommentPending = true;
     }
    }
    if (commentRejected) { pa.Section = "제외"; pa.Reason = "댓글 미작성"; }
    else if (valid.Count == 0 && !pa.Pending) { pa.Section = "제외"; pa.Reason = String.Join(", ", pa.Posts.Select(p => p.Reason).Where(r => r != "").Distinct()); if (pa.Reason == "") pa.Reason = "인정되는 글 없음"; }
    else if (pa.Pending || pa.CommentPending) pa.Section = "검토";
    else pa.Section = pa.Sum >= set.SpecialViews ? "특별" : "선착순";
    // 내보낼 때 구역 (검토가 남아 있으면 지금 값으로)
    pa.ExportSection = pa.Section == "검토" ? (valid.Count == 0 ? "제외" : (pa.Sum >= set.SpecialViews ? "특별" : "선착순")) : pa.Section;
    if (pa.Section == "검토" && valid.Count == 0) pa.Reason = "직접 검토 남음";
    // 3개월 선착순 블랙
    var b = hist == null ? null : hist.FindBlack(pa.Id, pa.Nick, set.BlackMonths, today);
    if (b != null) {
     pa.BlackInfo = History.Label(b);
     if (pa.ExportSection == "선착순") { pa.ExportSection = "제외"; pa.Reason = "최근 " + set.BlackMonths + "개월 선착순 당첨 (" + pa.BlackInfo + ")"; if (pa.Section == "선착순") pa.Section = "제외"; }
     else if (pa.ExportSection == "특별") pa.Note = "특별 미션 낙첨 시 선착순 불가 (최근 " + set.BlackMonths + "개월 선착순 당첨: " + pa.BlackInfo + ")";
    }
    if (pa.Section == "특별") sum.Special++; else if (pa.Section == "선착순") sum.First++; else if (pa.Section == "제외") sum.Excluded++; else sum.Review++;
   }
   return sum;
  }
  public static int ReviewCount(List<Participant> ps) { return ps.Sum(pa => pa.Posts.Count(p => p.State == PState.Review) + (pa.CommentPending ? 1 : 0)); }
  static string Closest(string nick, List<string> list) {
   string a = History.Key(nick); if (a == "") return ""; string best = ""; int bd = 99;
   foreach (var n in list) {
    string b = History.Key(n); int d;
    if (b.Length > 1 && (a.Contains(b) || b.Contains(a))) d = Math.Abs(a.Length - b.Length);
    else d = Lev(a, b);
    if (d < bd) { bd = d; best = n; }
   }
   return bd <= Math.Max(2, a.Length / 3) ? best : "";
  }
  static int Lev(string a, string b) {
   var d = new int[a.Length + 1, b.Length + 1];
   for (int i = 0; i <= a.Length; i++) d[i, 0] = i; for (int j = 0; j <= b.Length; j++) d[0, j] = j;
   for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++) d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
   return d[a.Length, b.Length];
  }
 }

 // ───────────────────────── xlsx 쓰기
 public class XOut {
  public class Cell { public string V = ""; public bool Num; public string F; public int S; }
  public string Name = "Sheet1";
  public SortedDictionary<int, SortedDictionary<int, Cell>> Rows = new SortedDictionary<int, SortedDictionary<int, Cell>>();
  public List<string> Merges = new List<string>(); public Dictionary<int, double> Widths = new Dictionary<int, double>(); public Dictionary<int, double> Heights = new Dictionary<int, double>();
  public List<KeyValuePair<string, string>> Links = new List<KeyValuePair<string, string>>();
  Cell At(int r, int c) { SortedDictionary<int, Cell> row; if (!Rows.TryGetValue(r, out row)) { row = new SortedDictionary<int, Cell>(); Rows[r] = row; } Cell x; if (!row.TryGetValue(c, out x)) { x = new Cell(); row[c] = x; } return x; }
  public void Set(int r, int c, string v, int s) { var x = At(r, c); x.V = v ?? ""; x.Num = false; x.F = null; x.S = s; }
  public void SetNum(int r, int c, double v, int s) { var x = At(r, c); x.V = v.ToString(CultureInfo.InvariantCulture); x.Num = true; x.F = null; x.S = s; }
  public void SetF(int r, int c, string f, string cached, int s) { var x = At(r, c); x.F = f; x.V = cached ?? ""; x.Num = false; x.S = s; }
  public void Style(int r, int c1, int c2, int s) { for (int c = c1; c <= c2; c++) At(r, c).S = s; }
  public void Merge(int r1, int c1, int r2, int c2) { Merges.Add(Col(c1) + r1 + ":" + Col(c2) + r2); }
  public void Link(int r, int c, string url) { if (!String.IsNullOrEmpty(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute)) Links.Add(new KeyValuePair<string, string>(Col(c) + r, url)); }
  public static string Col(int c) { string s = ""; while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; } return s; }
 }
 public class XStyles {
  readonly List<string> fonts = new List<string>(), fills = new List<string> { "<fill><patternFill patternType=\"none\"/></fill>", "<fill><patternFill patternType=\"gray125\"/></fill>" }, xfs = new List<string>();
  const string FontTail = "<name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/>";
  public XStyles() { Font(10, false, null, false); xfs.Add("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"); }
  public int Font(double size, bool bold, string rgb, bool underline) {
   string f = "<font>" + (bold ? "<b/>" : "") + (underline ? "<u/>" : "") + "<sz val=\"" + size.ToString(CultureInfo.InvariantCulture) + "\"/>" + (rgb != null ? "<color rgb=\"FF" + rgb + "\"/>" : "") + FontTail + "</font>";
   int i = fonts.IndexOf(f); if (i >= 0) return i; fonts.Add(f); return fonts.Count - 1;
  }
  public int Fill(string rgb) {
   if (rgb == null) return 0;
   string f = "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF" + rgb + "\"/><bgColor indexed=\"64\"/></patternFill></fill>";
   int i = fills.IndexOf(f); if (i >= 0) return i; fills.Add(f); return fills.Count - 1;
  }
  public int Xf(int font, int fill, bool border, string h, bool wrap, int numFmt) {
   string x = "<xf numFmtId=\"" + numFmt + "\" fontId=\"" + font + "\" fillId=\"" + fill + "\" borderId=\"" + (border ? 1 : 0) + "\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"" + h + "\" vertical=\"center\"" + (wrap ? " wrapText=\"1\"" : "") + "/></xf>";
   int i = xfs.IndexOf(x); if (i >= 0) return i; xfs.Add(x); return xfs.Count - 1;
  }
  public string Xml() {
   var b = "<border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"thin\"><color auto=\"1\"/></top><bottom style=\"thin\"><color auto=\"1\"/></bottom><diagonal/></border>";
   return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
    + "<fonts count=\"" + fonts.Count + "\">" + String.Concat(fonts) + "</fonts><fills count=\"" + fills.Count + "\">" + String.Concat(fills) + "</fills>"
    + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" + b + "</borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
    + "<cellXfs count=\"" + xfs.Count + "\">" + String.Concat(xfs) + "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"표준\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
  }
 }
 public static class XWriter {
  const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
  static string Esc(string s) { var sb = new StringBuilder(); foreach (char c in s ?? "") { if (c < 0x20 && c != '\n' && c != '\t') continue; switch (c) { case '&': sb.Append("&amp;"); break; case '<': sb.Append("&lt;"); break; case '>': sb.Append("&gt;"); break; case '"': sb.Append("&quot;"); break; default: sb.Append(c); break; } } return sb.ToString(); }
  static void Put(ZipArchive z, string name, string xml) { var e = z.CreateEntry(name, CompressionLevel.Optimal); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(xml); }
  public static void Save(string path, List<XOut> sheets, XStyles st) {
   string tmp = path + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp);
   using (var fs = new FileStream(tmp, FileMode.CreateNew)) using (var z = new ZipArchive(fs, ZipArchiveMode.Create)) {
    var ct = new StringBuilder(Head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
    var wb = new StringBuilder(Head + "<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\"><bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
    var wr = new StringBuilder(Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
    var used = new HashSet<string>();
    for (int i = 0; i < sheets.Count; i++) {
     int n = i + 1; string nm = SafeName(sheets[i].Name); string bn = nm; int k = 2; while (!used.Add(nm)) nm = (bn.Length > 28 ? bn.Substring(0, 28) : bn) + "(" + (k++) + ")";
     ct.Append("<Override PartName=\"/xl/worksheets/sheet" + n + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
     wb.Append("<sheet name=\"" + Esc(nm) + "\" sheetId=\"" + n + "\" r:id=\"rId" + n + "\"/>");
     wr.Append("<Relationship Id=\"rId" + n + "\" Type=\"" + NsRel + "/worksheet\" Target=\"worksheets/sheet" + n + ".xml\"/>");
     Put(z, "xl/worksheets/sheet" + n + ".xml", Sheet(sheets[i], i == 0));
     var links = sheets[i].Links;
     if (links.Count > 0) { var sr = new StringBuilder(Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"); for (int j = 0; j < links.Count; j++) sr.Append("<Relationship Id=\"rId" + (j + 1) + "\" Type=\"" + NsRel + "/hyperlink\" Target=\"" + Esc(links[j].Value) + "\" TargetMode=\"External\"/>"); Put(z, "xl/worksheets/_rels/sheet" + n + ".xml.rels", sr.Append("</Relationships>").ToString()); }
    }
    wr.Append("<Relationship Id=\"rId" + (sheets.Count + 1) + "\" Type=\"" + NsRel + "/styles\" Target=\"styles.xml\"/></Relationships>");
    Put(z, "[Content_Types].xml", ct.Append("</Types>").ToString());
    Put(z, "_rels/.rels", Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + NsRel + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
    Put(z, "xl/workbook.xml", wb.Append("</sheets><calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>").ToString());
    Put(z, "xl/_rels/workbook.xml.rels", wr.ToString());
    Put(z, "xl/styles.xml", st.Xml());
   }
   if (File.Exists(path)) File.Delete(path);
   File.Move(tmp, path);
  }
  public static string SafeName(string s) { s = Regex.Replace(s ?? "", @"[\[\]:*?/\\]", "-"); return s.Length > 31 ? s.Substring(0, 31) : (s.Length == 0 ? "Sheet1" : s); }
  static string Sheet(XOut x, bool first) {
   int maxR = x.Rows.Count > 0 ? x.Rows.Keys.Max() : 1, maxC = 1; foreach (var r in x.Rows.Values) if (r.Count > 0) maxC = Math.Max(maxC, r.Keys.Max());
   var sb = new StringBuilder(Head + "<worksheet xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\"><dimension ref=\"A1:" + XOut.Col(maxC) + maxR + "\"/><sheetViews><sheetView showGridLines=\"0\" " + (first ? "tabSelected=\"1\" " : "") + "workbookViewId=\"0\"><selection activeCell=\"A1\" sqref=\"A1\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"16.5\"/>");
   if (x.Widths.Count > 0) { sb.Append("<cols>"); foreach (var w in x.Widths.OrderBy(k => k.Key)) sb.Append("<col min=\"" + w.Key + "\" max=\"" + w.Key + "\" width=\"" + w.Value.ToString("0.##", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>"); sb.Append("</cols>"); }
   sb.Append("<sheetData>");
   foreach (var r in x.Rows) {
    double h; bool hh = x.Heights.TryGetValue(r.Key, out h);
    sb.Append("<row r=\"" + r.Key + "\"" + (hh ? " ht=\"" + h.ToString("0.##", CultureInfo.InvariantCulture) + "\" customHeight=\"1\"" : "") + ">");
    foreach (var c in r.Value) {
     string rf = XOut.Col(c.Key) + r.Key; var v = c.Value;
     if (v.F != null) sb.Append("<c r=\"" + rf + "\" s=\"" + v.S + "\" t=\"str\"><f>" + Esc(v.F) + "</f><v>" + Esc(v.V) + "</v></c>");
     else if (v.Num) sb.Append("<c r=\"" + rf + "\" s=\"" + v.S + "\"><v>" + v.V + "</v></c>");
     else if (v.V == "") sb.Append("<c r=\"" + rf + "\" s=\"" + v.S + "\"/>");
     else sb.Append("<c r=\"" + rf + "\" s=\"" + v.S + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Esc(v.V) + "</t></is></c>");
    }
    sb.Append("</row>");
   }
   sb.Append("</sheetData>");
   if (x.Merges.Count > 0) { sb.Append("<mergeCells count=\"" + x.Merges.Count + "\">"); foreach (var m in x.Merges) sb.Append("<mergeCell ref=\"" + m + "\"/>"); sb.Append("</mergeCells>"); }
   if (x.Links.Count > 0) { sb.Append("<hyperlinks>"); for (int i = 0; i < x.Links.Count; i++) sb.Append("<hyperlink ref=\"" + x.Links[i].Key + "\" r:id=\"rId" + (i + 1) + "\"/>"); sb.Append("</hyperlinks>"); }
   return sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>").ToString();
  }
 }

 // ───────────────────────── 당첨자 리스트 만들기
 public static class Exporter {
  public static string RoundText(string eventName) { var m = Regex.Match(eventName ?? "", @"(\d{1,2})월\s*(\d+)차"); return m.Success ? m.Groups[1].Value + "월 " + m.Groups[2].Value + "차" : ""; }
  public static string BaseName(Settings set, string eventName) {
   string ev = (eventName ?? "").Trim(), pre = (set.FilePrefix ?? "").Trim();
   if (pre != "") { var words = ev.Split(' ').ToList(); while (words.Count > 1 && pre.IndexOf(words[0], StringComparison.OrdinalIgnoreCase) >= 0) words.RemoveAt(0); ev = pre + " " + String.Join(" ", words); }
   return ev + " 당첨자 리스트";
  }
  public static string FileName(string baseName, DateTime today) { return Regex.Replace(baseName, @"[\\/:*?""<>|]", "_") + "_" + today.ToString("yyMMdd") + ".xlsx"; }
  // 기억해 둘 앞부분: 사용자가 고친 이름에서 이벤트 부분 앞
  public static string PrefixFrom(string edited, string eventName) {
   string b = Regex.Replace(edited ?? "", @"\s*당첨자 리스트\s*$", "").Trim();
   var words = (eventName ?? "").Split(' ').ToList();
   for (int i = 0; i < words.Count; i++) { string tail = String.Join(" ", words.Skip(i)); if (tail != "" && b.EndsWith(tail)) return b.Substring(0, b.Length - tail.Length).Trim(); }
   return "";
  }
  public static void Save(string path, string eventName, List<Participant> ps, Settings set) {
   var st = new XStyles();
   int fN = st.Font(10, false, null, false), fB = st.Font(10, true, null, false), fT = st.Font(12, true, "FFFFFF", false), fNote = st.Font(10, false, "467886", false);
   int black = st.Fill("000000");
   string[] secFill = { "CAEDFB", "FBE2D5", "FF0000" };
   int sTitle = st.Xf(fT, black, false, "center", false, 0), sHead = st.Xf(fB, 0, true, "center", true, 0), sNote = st.Xf(fNote, 0, false, "left", false, 0);
   var sC = new int[3]; var sL = new int[3]; var sNum = new int[3]; var sSec = new int[3];
   for (int i = 0; i < 3; i++) { int f = st.Fill(secFill[i]); sC[i] = st.Xf(fN, f, true, "center", false, 0); sL[i] = st.Xf(fN, f, true, "left", false, 0); sNum[i] = st.Xf(fN, f, true, "center", false, 3); sSec[i] = st.Xf(fB, f, true, "left", false, 0); }
   var x = new XOut(); string rt = RoundText(eventName);
   x.Name = rt != "" ? rt + " 소문내기 당첨자" : "당첨자 리스트";
   double[] w = { 0, 2.6, 6, 21, 18, 48, 8, 7, 16, 18, 26, 46 };
   for (int c = 1; c <= 11; c++) x.Widths[c] = w[c];
   int total = set.SpecialCount + set.FirstCount;
   x.Set(2, 2, eventName + " 당첨자 _ 경품인원 " + total + "명", sTitle); x.Style(2, 3, 11, sTitle); x.Merge(2, 2, 2, 11); x.Heights[2] = 24;
   string[] heads = { "번호", "응답일시", "커뮤니티 명칭", "외부 커뮤니티 링크", "조회수", "정성도", "공식카페 닉네임", "네이버 아이디", "참여 인정 여부\n(※특별 미션 기준 / 조회수 합산 " + set.SpecialViews + " 이상)", "특이사항" };
   for (int i = 0; i < heads.Length; i++) x.Set(3, 2 + i, heads[i], sHead); x.Heights[3] = 33;
   int r = 4;
   var special = ps.Where(p => p.ExportSection == "특별").OrderBy(p => p.No).ToList();
   var first = ps.Where(p => p.ExportSection == "선착순").OrderBy(p => p.No).ToList();
   // 특별
   x.Set(r, 2, "특별 미션 경품_" + set.SpecialCount + "명" + (set.SpecialPrize != "" ? " (" + set.SpecialPrize + ")" : ""), sSec[0]); x.Style(r, 3, 3, sSec[0]); x.Merge(r, 2, r, 3);
   x.Set(r, 5, "*금번 당첨자는 추첨 이후 노란색으로 표기", sNote); r++;
   int n = 0;
   foreach (var p in special) { string note = p.Note != "" ? p.Note : "특별 미션 낙첨 시 선착순 경품 인원으로 이동"; if (p.Section == "검토") note += " / 직접 검토 남음"; r = Person(x, r, ++n, p, 0, set.SpecialViews, note, sC, sL, sNum); }
   // 선착순
   int firstCount = set.FirstCount > 0 ? Math.Min(set.FirstCount, first.Count) : first.Count;
   x.Set(r, 2, "선착순 경품_" + firstCount + "명" + (set.FirstPrize != "" ? " (" + set.FirstPrize + ")" : ""), sSec[1]); x.Style(r, 3, 3, sSec[1]); x.Merge(r, 2, r, 3); r++;
   n = 0;
   foreach (var p in first) { n++; string note = set.FirstCount > 0 && n > set.FirstCount ? "선착순 인원 밖 (특별 미션 추첨 후 확정)" : ""; if (p.Section == "검토") note = (note == "" ? "" : note + " / ") + "직접 검토 남음"; r = Person(x, r, n, p, 1, set.MinViews, note, sC, sL, sNum); }
   r++;
   // 제외: 통째로 빠진 사람 + 인정된 사람의 빠진 글
   var exRows = new List<KeyValuePair<Participant, Post>>();
   foreach (var p in ps.OrderBy(q => q.No)) {
    if (p.ExportSection == "제외") exRows.Add(new KeyValuePair<Participant, Post>(p, null));
    else foreach (var post in p.Posts.Where(q => q.State == PState.Excluded)) exRows.Add(new KeyValuePair<Participant, Post>(p, post));
   }
   x.Set(r, 2, "경품제외대상_" + exRows.Count + "명 (추첨진행 X)", sSec[2]); x.Style(r, 3, 3, sSec[2]); x.Merge(r, 2, r, 3); r++;
   n = 0;
   foreach (var kv in exRows) {
    var p = kv.Key; n++;
    if (kv.Value == null) {
     var posts = p.Posts.Count > 0 ? p.Posts : new List<Post> { new Post { P = p } };
     bool isBlack = p.Reason.Contains("선착순 당첨");
     int r1 = r;
     foreach (var post in posts) {
      Row(x, r, 2, post, sC, sL, sNum);
      if (r == r1) { x.SetNum(r, 2, n, sC[2]); x.Set(r, 3, p.Time, sC[2]); x.Set(r, 8, p.Nick, sC[2]); x.Set(r, 9, p.Id, sC[2]); x.Set(r, 10, isBlack ? "참여 인정" : "미달", sC[2]); x.Set(r, 11, p.Reason, sL[2]); }
      else { x.Set(r, 2, "", sC[2]); x.Set(r, 3, "", sC[2]); x.Set(r, 8, "", sC[2]); x.Set(r, 9, "", sC[2]); x.Set(r, 10, "", sC[2]); x.Set(r, 11, post.Reason != "" && post.Reason != p.Reason ? post.Reason : "", sL[2]); }
      r++;
     }
    } else {
     Row(x, r, 2, kv.Value, sC, sL, sNum);
     x.SetNum(r, 2, n, sC[2]); x.Set(r, 3, p.Time, sC[2]); x.Set(r, 8, p.Nick, sC[2]); x.Set(r, 9, p.Id, sC[2]); x.Set(r, 10, "미달", sC[2]); x.Set(r, 11, kv.Value.Reason, sL[2]);
     r++;
    }
   }
   r++;
   string[] legend = { "특별 미션 경품 대상인원", "선착순 경품 인원", "참여로 인정 안되는 인원 (참여조건 충족 X)" };
   for (int i = 0; i < 3; i++) { x.Set(r, 2, legend[i], sSec[i]); x.Style(r, 3, 3, sSec[i]); x.Merge(r, 2, r, 3); r++; }
   XWriter.Save(path, new List<XOut> { x }, st);
  }
  static int Person(XOut x, int r, int no, Participant p, int sec, int threshold, string note, int[] sC, int[] sL, int[] sNum) {
   var posts = p.Posts.Where(q => q.State != PState.Excluded).ToList();
   if (posts.Count == 0) posts.Add(new Post { P = p });
   int r1 = r, r2 = r + posts.Count - 1;
   foreach (var post in posts) {
    Row(x, r, sec, post, sC, sL, sNum);
    if (r == r1) {
     x.SetNum(r, 2, no, sC[sec]); x.Set(r, 3, p.Time, sC[sec]); x.Set(r, 8, p.Nick, sC[sec]); x.Set(r, 9, p.Id, sC[sec]);
     x.SetF(r, 10, "IF(" + threshold + "<=SUM(F" + r1 + ":F" + r2 + "),\"참여 인정\",\"미달\")", p.Sum >= threshold ? "참여 인정" : "미달", sC[sec]);
     x.Set(r, 11, note, sL[sec]);
    } else { foreach (int c in new[] { 2, 3, 8, 9, 10 }) x.Set(r, c, "", sC[sec]); x.Set(r, 11, "", sL[sec]); }
    r++;
   }
   return r;
  }
  static void Row(XOut x, int r, int sec, Post post, int[] sC, int[] sL, int[] sNum) {
   x.Set(r, 4, post.Community, sC[sec]);
   string url = post.Url ?? ""; x.Set(r, 5, url, sL[sec]); x.Link(r, 5, url);
   if (post.EffViews >= 0) x.SetNum(r, 6, post.EffViews, sNum[sec]); else x.Set(r, 6, "-", sC[sec]);
   x.Set(r, 7, post.EffQuality != "" ? post.EffQuality : "-", sC[sec]);
  }
 }

 // ───────────────────────── 최종 당첨자 리스트(노란색 칠한 파일) → 이력
 public class ImportResult { public string Title = "", Round = ""; public int Month; public List<HEntry> Entries = new List<HEntry>(); }
 public static class ListImporter {
  public static ImportResult Read(string path) {
   var book = XBook.Read(path);
   var sh = book.Sheets.FirstOrDefault(s => s.Rows.Values.Any(r => r.Values.Any(c => c.Text.Contains("특별 미션 경품_")))) ?? book.Sheets.FirstOrDefault();
   var res = new ImportResult(); if (sh == null) return res;
   int mode = 0; int cNick = 8, cId = 9, cNote = 11;
   foreach (var row in sh.Rows) {
    string all = String.Join(" ", row.Value.Values.Select(v => v.Text));
    if (res.Title == "" && all.Contains("당첨자")) res.Title = all.Trim();
    foreach (var c in row.Value) { if (c.Value.Text.Contains("공식카페 닉네임")) cNick = c.Key; else if (c.Value.Text.Trim() == "네이버 아이디") cId = c.Key; else if (c.Value.Text.Trim() == "특이사항") cNote = c.Key; }
    string b = sh.Cell(row.Key, 2);
    if (b.StartsWith("특별 미션 경품_")) { mode = 1; continue; }
    if (b.StartsWith("선착순 경품_")) { mode = 2; continue; }
    if (b.StartsWith("경품제외") || b.StartsWith("특별 미션 경품 대상")) { mode = 0; continue; }
    if (mode == 0) continue;
    string nick = sh.Cell(row.Key, cNick).Trim(), id = sh.Cell(row.Key, cId).Trim(), note = sh.Cell(row.Key, cNote);
    if (nick == "" && id == "") continue;
    bool yellow = false; foreach (var c in row.Value) if (book.IsYellow(c.Value.Style)) { yellow = true; break; }
    string kind;
    if (mode == 1) kind = yellow ? "특별" : (note.Contains("선착순 불가") ? "" : "선착순");
    else kind = note.Contains("선착순 인원 밖") ? "" : "선착순";
    if (kind == "") continue;
    res.Entries.Add(new HEntry { Kind = kind, Nick = nick, Id = id });
   }
   var m = Regex.Match(res.Title + " " + sh.Name + " " + System.IO.Path.GetFileName(path), @"(\d{1,2})월\D{0,12}?(\d+)차");
   if (m.Success) { res.Month = Int32.Parse(m.Groups[1].Value); res.Round = m.Groups[2].Value + "차"; }
   return res;
  }
 }

 // ───────────────────────── 월별 정리 엑셀
 public static class HistoryExport {
  public static void Save(string path, History h, int months, DateTime today) {
   var st = new XStyles();
   int black = st.Fill("000000"), fW = st.Font(9, true, "FFFFFF", false), fN = st.Font(9, false, null, false);
   int sTitle = st.Xf(st.Font(11, true, "FFFFFF", false), black, false, "center", false, 0), sHead = st.Xf(fW, black, true, "center", false, 0), sC = st.Xf(fN, 0, true, "center", false, 0);
   var sheets = new List<XOut>();
   foreach (var g in h.Items.GroupBy(it => it.Ym).OrderByDescending(gg => gg.Key)) {
    var x = new XOut(); x.Name = History.Label(new HEntry { Ym = g.Key });
    double[] w = { 0, 2.6, 10, 10, 18, 18, 13, 16 }; for (int c = 1; c <= 7; c++) x.Widths[c] = w[c];
    x.Set(2, 2, x.Name + " 당첨 이력 (선착순 " + g.Count(it => it.Kind == "선착순") + "명 · 특별 " + g.Count(it => it.Kind == "특별") + "명)", sTitle); x.Style(2, 3, 7, sTitle); x.Merge(2, 2, 2, 7); x.Heights[2] = 22;
    string[] hd = { "회차", "구분", "닉네임", "네이버 아이디", "발표일", "선착순 제외" };
    for (int i = 0; i < hd.Length; i++) x.Set(3, 2 + i, hd[i], sHead);
    int r = 4;
    foreach (var e in g.OrderBy(it => it.Round).ThenBy(it => it.Kind == "특별" ? 0 : 1).ThenBy(it => it.Nick)) {
     x.Set(r, 2, e.Round, sC); x.Set(r, 3, e.Kind, sC); x.Set(r, 4, e.Nick, sC); x.Set(r, 5, e.Id, sC); x.Set(r, 6, e.Date.ToString("yyyy-MM-dd"), sC);
     x.Set(r, 7, e.Kind == "선착순" ? (History.Until(e, months) > today.Date ? History.Until(e, months).ToString("yyyy-MM-dd") + "까지" : "풀림") : "없음", sC);
     r++;
    }
    sheets.Add(x);
   }
   if (sheets.Count == 0) { var x = new XOut { Name = "당첨 이력" }; x.Set(2, 2, "기록이 없어요", 0); sheets.Add(x); }
   XWriter.Save(path, sheets, st);
  }
 }
}
