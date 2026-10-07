using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using System.Xml;
using System.Web;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace BlogSearcher {
 public class Profile {
  public string Id, Name, Url, Latest = "", Evidence = "";
  public List<string> Titles = new List<string>();
  public Dictionary<string,double> Terms = new Dictionary<string,double>();
  public double Score;
 }
 public class Hit { public string Id, Title, Query; }
 public class SearchReport {
  public List<Profile> Profiles = new List<Profile>();
  public List<string> Warnings = new List<string>();
 }
 public static class Engine {
  static readonly Regex Urls = new Regex(@"(?:https?://)?(?:(?:m\.)?blog\.naver\.com/[^\s<>""']+|[\w-]+\.blog\.me(?:/[^\s<>""']*)?)", RegexOptions.IgnoreCase);
  static readonly HashSet<string> Stop = new HashSet<string>(("네이버 블로그 오늘 이번 정말 너무 그리고 그래서 하지만 있는 없는 하는 좋은 같이 바로 조금 다시 소개 후기 리뷰 제품 광고 협찬 포스팅 사진 사용 여러분 우리 제가 저의 나의 맛있는 만드는 만들기 레시피 방법 추천 일상 이야기 com naver blog http https www 합니다 입니다 있어요 했어요 먹어요 있어 있는것 같아요 때문에 통해 대한 위한 이제 최근 그냥 함께 많이 가장 어떻게 이렇게 저희 직접 요즘 한번 정도 새로운 정보 확인 다양한 시간 하루 시작 생각 준비 알려 드리는 더보기 원래 이름 제목 주소 검색 결과 저장 링크 새창 글쓰기 댓글 공감 이웃 구독 자신 다른 무엇 있다 없다 된다 했다 에서 으로 인데 하고 하며 하면 또는 이것 그것 이런 저런 해당 관련 제공 가능 내용 사람 필요 다음 처음" ).Split(' '));
  public static string Normalize(string raw) {
   if(String.IsNullOrWhiteSpace(raw))return null;
   raw=WebUtility.HtmlDecode(raw.Trim()).TrimEnd('.',',',')',']',';');
   if(!raw.StartsWith("http",StringComparison.OrdinalIgnoreCase))raw="https://"+raw;
   Uri uri; if(!Uri.TryCreate(raw,UriKind.Absolute,out uri))return null;
   string host=uri.Host.ToLowerInvariant(), id="";
   if(host.EndsWith(".blog.me"))id=host.Substring(0,host.Length-8);
   else if(host=="blog.naver.com"||host=="m.blog.naver.com") {
    id=HttpUtility.ParseQueryString(uri.Query)["blogId"];
    if(String.IsNullOrEmpty(id))id=uri.AbsolutePath.Trim('/').Split('/')[0];
   } else return null;
   if(!Regex.IsMatch(id??"",@"^[A-Za-z0-9_-]{2,50}$") || new[]{"PostView","PostList","ClipList","influencer_search"}.Contains(id,StringComparer.OrdinalIgnoreCase))return null;
   return "https://blog.naver.com/"+id.ToLowerInvariant();
  }
  public static List<string> ParseUrls(string text) {
   return Urls.Matches(text??"").Cast<Match>().Select(m=>Normalize(m.Value)).Where(x=>x!=null).Distinct().ToList();
  }
  public static void SaveAddresses(string path,IEnumerable<string> urls) {
   var cleaned=urls.Select(Normalize).Where(u=>u!=null).Distinct().ToArray();
   if(cleaned.Length==0)throw new InvalidDataException("저장할 블로그 주소가 없습니다.");
   File.WriteAllLines(path,cleaned,new UTF8Encoding(true));
  }
  public static string Clean(string s) {
   return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(s??"",@"<[^>]*>"," ")),@"\s+"," ").Trim();
  }
  public static Dictionary<string,double> Terms(string text) {
   var result=new Dictionary<string,double>();
   foreach(Match m in Regex.Matches(Clean(text).ToLowerInvariant(),@"[가-힣]{2,12}|[a-z]{3,20}")) {
    string t=m.Value; if(Stop.Contains(t))continue;
    if(t.EndsWith("입니다")||t.EndsWith("했어요")||t.EndsWith("하는")||t.EndsWith("해요"))continue;
    if(!result.ContainsKey(t))result[t]=0;result[t]++;
   } return result;
  }
  public static double Similarity(Dictionary<string,double> a,Dictionary<string,double> b) {
   if(a.Count==0||b.Count==0)return 0;
   double dot=a.Where(x=>b.ContainsKey(x.Key)).Sum(x=>x.Value*b[x.Key]);
   return dot/(Math.Sqrt(a.Values.Sum(x=>x*x))*Math.Sqrt(b.Values.Sum(x=>x*x)));
  }
  public static string Get(string url,CancellationToken ct,string clientId="",string secret="") {
   ct.ThrowIfCancellationRequested();
   var req=(HttpWebRequest)WebRequest.Create(url);req.Timeout=15000;req.ReadWriteTimeout=15000;
   req.UserAgent="BlogSearcher/1.0";req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   req.Referer="https://m.blog.naver.com/"+url.Split('/').Last();
   if(clientId.Length>0){req.Headers["X-Naver-Client-Id"]=clientId;req.Headers["X-Naver-Client-Secret"]=secret;}
   using(ct.Register(()=>req.Abort())) {
    try { using(var r=req.GetResponse())using(var s=r.GetResponseStream())using(var sr=new StreamReader(s,Encoding.UTF8)) {return sr.ReadToEnd();} }
    catch(WebException) {ct.ThrowIfCancellationRequested();throw;}
   }
  }
  public static Profile ReadRss(string id,string xml) {
   var doc=new XmlDocument();doc.XmlResolver=null;
   using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc.Load(reader);
   var channel=doc.SelectSingleNode("/rss/channel");if(channel==null)throw new InvalidDataException("공개 RSS 형식이 아닙니다.");
   var p=new Profile {Id=id,Url="https://blog.naver.com/"+id,Name=Clean(channel.SelectSingleNode("title")==null?id:channel.SelectSingleNode("title").InnerText)};
   DateTimeOffset latest=DateTimeOffset.MinValue;
   foreach(XmlNode item in channel.SelectNodes("item").Cast<XmlNode>().Take(25)) {
    var title=item.SelectSingleNode("title");if(title!=null)p.Titles.Add(Clean(title.InnerText));
    var date=item.SelectSingleNode("pubDate");DateTimeOffset parsed;
    if(date!=null&&DateTimeOffset.TryParse(date.InnerText,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AllowWhiteSpaces,out parsed)&&parsed>latest)latest=parsed;
   }
   if(latest!=DateTimeOffset.MinValue)p.Latest=latest.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd");
   p.Terms=Terms(String.Join(" ",p.Titles));return p;
  }
  public static Profile FetchProfile(string url,CancellationToken ct,Action<string> log) {
   string id=url.Split('/').Last();var p=ReadRss(id,Get("https://rss.blog.naver.com/"+id+".xml",ct));
   if(p.Titles.Count==0)throw new InvalidDataException("공개 최근 글이 없습니다.");
   try {
    var json=new JavaScriptSerializer().DeserializeObject(Get("https://m.blog.naver.com/api/blogs/"+id,ct)) as Dictionary<string,object>;
    var result=json!=null&&json.ContainsKey("result")?json["result"] as Dictionary<string,object>:null;
    if(result!=null&&result.ContainsKey("nickName"))p.Name=Convert.ToString(result["nickName"]);
   }catch(OperationCanceledException){throw;}catch(Exception){log(id+": 닉네임 조회 제한, RSS 블로그명을 표시합니다.");}
   return p;
  }
  public static List<Hit> ReadPublicSearch(string html,string query) {
   var hits=new Dictionary<string,Hit>();
   html=WebUtility.HtmlDecode(html);
   foreach(Match m in Regex.Matches(html,@"<a\b[^>]*href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<text>.*?)</a>",RegexOptions.IgnoreCase|RegexOptions.Singleline)) {
    string u=Normalize(m.Groups["url"].Value);if(u==null)continue;
    string id=u.Split('/').Last(), t=Clean(m.Groups["text"].Value);
    if(!hits.ContainsKey(id))hits[id]=new Hit {Id=id,Title=t,Query=query};
    else if(t.Length>hits[id].Title.Length)hits[id].Title=t;
   }
   // 네이버 검색의 JSON 렌더 데이터에도 공개 게시글 링크가 있습니다.
   string decoded=html.Replace("\\/","/");
   foreach(string u in ParseUrls(decoded)) {
    string id=u.Split('/').Last();if(!hits.ContainsKey(id))hits[id]=new Hit {Id=id,Title="",Query=query};
   }
   return hits.Values.ToList();
  }
  public static List<Hit> Search(string query,string clientId,string secret,CancellationToken ct) {
   if(clientId.Length==0) {
    string html=Get("https://search.naver.com/search.naver?where=blog&query="+Uri.EscapeDataString(query),ct);
    if(html.Contains("자동입력 방지")||html.Contains("비정상적인 접근"))throw new InvalidOperationException("검색 접근이 제한됐습니다. 잠시 후 다시 시도하거나 공식 검색 API를 연결하세요.");
    return ReadPublicSearch(html,query);
   }
   string response=Get("https://openapi.naver.com/v1/search/blog.json?display=100&sort=sim&query="+Uri.EscapeDataString(query),ct,clientId,secret);
   var d=new JavaScriptSerializer().DeserializeObject(response) as Dictionary<string,object>;
   var list=new List<Hit>();
   foreach(var obj in (object[])d["items"]) {
    var x=(Dictionary<string,object>)obj;string url=Normalize(Convert.ToString(x["bloggerlink"]))??Normalize(Convert.ToString(x["link"]));
    if(url!=null)list.Add(new Hit {Id=url.Split('/').Last(),Title=Clean(Convert.ToString(x["title"])),Query=query});
   }return list;
  }
  public static SearchReport Run(List<string> references,HashSet<string> excluded,string keywords,int target,string clientId,string secret,CancellationToken ct,Action<string> log) {
   var report=new SearchReport();var refs=new List<Profile>();
   try {
    foreach(string url in references) {
     log("레퍼런스 분석: "+url);
     try {refs.Add(FetchProfile(url,ct,log));}catch(OperationCanceledException){throw;}catch(Exception e){report.Warnings.Add(url+": "+e.Message);log("레퍼런스 확인 실패: "+url+" / "+e.Message);}
    }
    if(refs.Count==0){report.Warnings.Add("분석 가능한 레퍼런스가 없습니다. 공개 글이 있는 블로그 주소를 넣어 주세요.");return report;}
    var combined=new Dictionary<string,double>();
    foreach(var p in refs)foreach(var kv in p.Terms){if(!combined.ContainsKey(kv.Key))combined[kv.Key]=0;combined[kv.Key]+=kv.Value;}
    var top=combined.OrderByDescending(x=>x.Value).ThenBy(x=>x.Key).Take(12).Select(x=>x.Key).ToList();
    log("레퍼런스 주요 단어: "+String.Join(", ",top));
    var explicitQueries=keywords.Split(new[]{',',';','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
    var queries=explicitQueries.Concat(top.Take(5)).Distinct().Take(10).ToList();
    if(explicitQueries.Count>0)queries=explicitQueries.Concat(top.Take(4).Select(t=>explicitQueries[0]+" "+t)).Distinct().Take(10).ToList();
    if(queries.Count==0){report.Warnings.Add("검색어를 만들 수 없습니다. 추가 키워드를 입력해 주세요.");return report;}
    var hits=new Dictionary<string,List<Hit>>();
    foreach(string q in queries) {
     ct.ThrowIfCancellationRequested();log("웹 검색: "+q);
     try {foreach(var h in Search(q,clientId,secret,ct)) {string u="https://blog.naver.com/"+h.Id;if(references.Contains(u)||excluded.Contains(u))continue;if(!hits.ContainsKey(h.Id))hits[h.Id]=new List<Hit>();hits[h.Id].Add(h);}}
     catch(OperationCanceledException){throw;}catch(Exception e){report.Warnings.Add("검색 '"+q+"': "+e.Message);log("검색 오류: "+e.Message);}
     if(ct.WaitHandle.WaitOne(850))ct.ThrowIfCancellationRequested();
    }
    log("중복·제외 목록 제거 후 "+hits.Count+"개 발견. 최근 글을 확인합니다.");
    foreach(var pair in hits.OrderByDescending(x=>x.Value.Select(h=>h.Query).Distinct().Count()).Take(Math.Min(target*3,300))) {
     ct.ThrowIfCancellationRequested();
     try {
      var p=FetchProfile("https://blog.naver.com/"+pair.Key,ct,log);
      double best=refs.Max(r=>Similarity(r.Terms,p.Terms));
      var overlap=top.Where(t=>p.Terms.ContainsKey(t)).Take(5).ToList();
      if(best<=0||overlap.Count==0)continue;
      p.Score=Math.Round(best*100,1);
      p.Evidence="공통 주제: "+String.Join(", ",overlap)+" / "+p.Titles.FirstOrDefault();
      report.Profiles.Add(p);log("후보 확인 "+report.Profiles.Count+": "+p.Name+" (주제 겹침 "+p.Score.ToString("0.0")+")");
     }catch(OperationCanceledException){throw;}catch(Exception e){log("후보 제외: "+pair.Key+" / "+e.Message);}
     if(ct.WaitHandle.WaitOne(300))ct.ThrowIfCancellationRequested();
    }
   }catch(OperationCanceledException){report.Warnings.Add("검색을 중단했습니다. 중단 전 확인된 후보를 표시합니다.");}
   report.Profiles=report.Profiles.OrderByDescending(p=>p.Score).Take(target).ToList();return report;
  }
 }
 public class CardPanel:Panel {
  public CardPanel(){DoubleBuffered=true;BackColor=Color.White;Padding=new Padding(18);}
  protected override void OnPaintBackground(PaintEventArgs e){
   e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var path=Round(new Rectangle(0,0,Width-1,Height-1),14))using(var brush=new SolidBrush(BackColor))using(var pen=new Pen(Color.FromArgb(226,226,235))){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
  }
  public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 }
 public class SoftButton:Button {
  public SoftButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Height=34;BackColor=Color.FromArgb(239,239,244);ForeColor=Color.FromArgb(35,35,38);Font=new Font("맑은 고딕",9,FontStyle.Bold);}
  protected override void OnResize(EventArgs e){base.OnResize(e);if(Width>20&&Height>20){var old=Region;using(var p=CardPanel.Round(new Rectangle(0,0,Width,Height),9))Region=new Region(p);if(old!=null)old.Dispose();}}
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var p=CardPanel.Round(new Rectangle(0,0,Width-1,Height-1),Math.Min(9,Math.Min(Width,Height)/2)))using(var b=new SolidBrush(BackColor))e.Graphics.FillPath(b,p);
   TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Color.FromArgb(155,155,163),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  }
 }
 public class BrandMark:Control {
  public BrandMark(){Size=new Size(28,30);}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var b=new SolidBrush(Color.FromArgb(255,104,26)))for(int i=0;i<3;i++){int x=3+i*4,y=23-i*8;e.Graphics.FillPolygon(b,new[]{new Point(x,y),new Point(x+13,y),new Point(x+19,y-6),new Point(x+6,y-6)});}}
 }
 public class MainForm:Form {
  TextBox refs=new TextBox(),keywords=new TextBox(),logs=new TextBox(),apiId=new TextBox(),apiSecret=new TextBox();
  Button run=new SoftButton(),cancel=new SoftButton(),export=new SoftButton(),loadRefs=new SoftButton(),loadExcluded=new SoftButton();
  NumericUpDown target=new NumericUpDown();DataGridView grid=new DataGridView();Label status=new Label(),excludedLabel=new Label();
  HashSet<string> excluded=new HashSet<string>();CancellationTokenSource cts;bool busy;
  Label resultCount=new Label(),refCount=new Label(),selectionCount=new Label();Panel logPanel=new Panel();
  static readonly Color Orange=Color.FromArgb(255,104,26),Ink=Color.FromArgb(30,30,32),Muted=Color.FromArgb(126,126,135),Surface=Color.FromArgb(242,242,248);
  Label Caption(string text,int size=9,bool bold=false){return new Label{Text=text,AutoSize=true,ForeColor=Ink,Font=new Font("맑은 고딕",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,6,0,4)};}
  Button Small(string text,Action action){var b=new SoftButton{Text=text,AutoSize=true,Padding=new Padding(9,0,9,0),Margin=new Padding(0,2,8,2)};b.Click+=(s,e)=>action();return b;}
  void UpdateCounts(){refCount.Text=Engine.ParseUrls(refs.Text).Count+"개";resultCount.Text="총 "+grid.Rows.Count+"명";selectionCount.Text="선택 "+grid.Rows.Cast<DataGridViewRow>().Count(r=>Convert.ToBoolean(r.Cells[0].Value??false))+"명";}
  void Settings(){using(var d=new Form{Text="검색 설정",Size=new Size(440,300),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font,BackColor=Color.White}){
   var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),FlowDirection=FlowDirection.TopDown,WrapContents=false};d.Controls.Add(layout);
   layout.Controls.Add(Caption("기본 웹 검색 · API 연결은 선택입니다",10,true));
   var id=new TextBox{Width=370,Text=apiId.Text};var secret=new TextBox{Width=370,Text=apiSecret.Text,UseSystemPasswordChar=true};
   layout.Controls.Add(Caption("Client ID"));layout.Controls.Add(id);layout.Controls.Add(Caption("Client Secret"));layout.Controls.Add(secret);
   var row=new FlowLayoutPanel{Width=375,Height=42};row.Controls.Add(Small("적용",()=>{apiId.Text=id.Text.Trim();apiSecret.Text=secret.Text.Trim();d.Close();}));row.Controls.Add(Small("발급 안내",()=>Open("https://developers.naver.com/docs/serviceapi/search/blog/blog.md")));layout.Controls.Add(row);
   layout.Controls.Add(new Label{Text="API 키는 파일로 저장하지 않습니다.",AutoSize=true,ForeColor=Muted});d.ShowDialog(this);
  }}
  public void SavePreview(string path) {
   refs.Text="https://blog.naver.com/baby0817\r\nhttps://blog.naver.com/yummycook";
   keywords.Text="김장, 한식, 집밥";
   grid.Rows.Add(true,"예시 블로거 A","https://blog.naver.com/example_a",34.2,"2026-10-07","공통 주제: 배추김치, 집밥 / 김장 준비와 고춧가루 고르는 법");
   grid.Rows.Add(false,"예시 블로거 B","https://blog.naver.com/example_b",28.7,"2026-10-06","공통 주제: 한식, 반찬 / 오늘의 집밥과 제철 반찬");UpdateCounts();refs.Select(0,0);
   ShowInTaskbar=false;Opacity=0;Show();Application.DoEvents();PerformLayout();
   grid.ClearSelection();ActiveControl=null;Application.DoEvents();
   using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Close();
  }
  public MainForm() {
   Text="블로거 서쳐";Size=new Size(1320,820);MinimumSize=new Size(1130,740);StartPosition=FormStartPosition.CenterScreen;
   Font=new Font("맑은 고딕",9);BackColor=Surface;ForeColor=Ink;AutoScaleMode=AutoScaleMode.Dpi;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
   shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);
   var sidebar=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=Padding.Empty};shell.Controls.Add(sidebar,0,0);
   sidebar.Controls.Add(new BrandMark{Location=new Point(20,24)});
   sidebar.Controls.Add(new Label{Text="블로거 서쳐",Location=new Point(56,25),AutoSize=true,Font=new Font("맑은 고딕",11,FontStyle.Bold)});
   sidebar.Controls.Add(new Label{Text="버전 1.1",Location=new Point(57,49),AutoSize=true,ForeColor=Muted,Font=new Font("맑은 고딕",8)});
   sidebar.Controls.Add(new Label{Text="블로그",Location=new Point(22,101),AutoSize=true,ForeColor=Muted});
   var active=Small("블로거 탐색",()=>refs.Focus());active.Location=new Point(12,128);active.Size=new Size(156,40);active.AutoSize=false;active.BackColor=Ink;active.ForeColor=Color.White;sidebar.Controls.Add(active);
   var saveNav=Small("TXT 내보내기",()=>Export());saveNav.Location=new Point(12,175);saveNav.Size=new Size(156,38);saveNav.AutoSize=false;saveNav.BackColor=Color.White;sidebar.Controls.Add(saveNav);
   var companion=new Panel{Height=78,Dock=DockStyle.Bottom,Padding=new Padding(18),BackColor=Surface};companion.Controls.Add(new Label{Text="네이버 크롤러 연계\n주소만 한 줄씩 TXT 저장",Dock=DockStyle.Fill,ForeColor=Muted});sidebar.Controls.Add(companion);
   var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24,18,24,6),Margin=Padding.Empty};
   main.RowStyles.Add(new RowStyle(SizeType.Absolute,74));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));main.RowStyles.Add(new RowStyle(SizeType.Absolute,0));main.RowStyles.Add(new RowStyle(SizeType.Absolute,30));shell.Controls.Add(main,1,0);
   var header=new Panel{Dock=DockStyle.Fill};header.Controls.Add(new Label{Text="블로거 탐색",Location=new Point(0,1),AutoSize=true,Font=new Font("맑은 고딕",19,FontStyle.Bold)});
   header.Controls.Add(new Label{Text="레퍼런스와 비슷한 블로거를 찾고, 크롤러에 넘길 주소를 모읍니다.",Location=new Point(1,39),AutoSize=true,ForeColor=Muted});
   var settings=Small("검색 설정",()=>Settings());settings.Anchor=AnchorStyles.Top|AnchorStyles.Right;settings.Location=new Point(header.Width-100,5);header.Controls.Add(settings);header.Resize+=(s,e)=>settings.Left=header.ClientSize.Width-settings.Width;main.Controls.Add(header,0,0);
   var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,294));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));main.Controls.Add(body,0,1);
   var input=new CardPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,8,0)};body.Controls.Add(input,0,0);
   var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=11,Margin=Padding.Empty};input.Controls.Add(left);
   int[] heights={36,0,34,25,34,37,34,25,44,36,24};
   for(int i=0;i<heights.Length;i++)left.RowStyles.Add(new RowStyle(i==1?SizeType.Percent:SizeType.Absolute,i==1?100:heights[i]));
   var inputHead=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};inputHead.Controls.Add(Caption("레퍼런스",10,true));
   refCount.AutoSize=true;refCount.ForeColor=Orange;refCount.Padding=new Padding(7,6,0,0);inputHead.Controls.Add(refCount);
   loadRefs.Text="파일 불러오기";loadRefs.AutoSize=false;loadRefs.Size=new Size(99,29);loadRefs.Margin=new Padding(13,0,0,0);loadRefs.Click+=(s,e)=>Import(false);inputHead.Controls.Add(loadRefs);left.Controls.Add(inputHead,0,0);
   refs.Multiline=true;refs.ScrollBars=ScrollBars.Vertical;refs.Dock=DockStyle.Fill;refs.BorderStyle=BorderStyle.None;refs.BackColor=Surface;refs.Font=new Font("맑은 고딕",9);refs.Margin=new Padding(0,4,0,6);refs.TextChanged+=(s,e)=>UpdateCounts();left.Controls.Add(refs,0,1);
   var edits=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
   foreach(var pair in new[]{new[]{"붙여넣기","paste"},new[]{"중복 제거","dedup"},new[]{"비우기","clear"}}){string action=pair[1];var b=Small(pair[0],()=>{if(action=="paste"){try{if(Clipboard.ContainsText())refs.AppendText((refs.Text.Length>0?Environment.NewLine:"")+Clipboard.GetText());}catch(Exception ex){MessageBox.Show(ex.Message);}}else if(action=="dedup")refs.Text=String.Join(Environment.NewLine,Engine.ParseUrls(refs.Text));else refs.Clear();});b.ForeColor=Orange;b.BackColor=Color.White;b.Font=new Font("맑은 고딕",8,FontStyle.Bold);b.Padding=Padding.Empty;b.Margin=new Padding(0,0,8,0);edits.Controls.Add(b);}left.Controls.Add(edits,0,2);
   left.Controls.Add(Caption("추가 키워드",9,true),0,3);keywords.Dock=DockStyle.Fill;keywords.BackColor=Surface;keywords.BorderStyle=BorderStyle.FixedSingle;keywords.Margin=new Padding(0,3,0,5);left.Controls.Add(keywords,0,4);
   var count=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};count.Controls.Add(Caption("최대 후보"));target.Minimum=10;target.Maximum=200;target.Value=50;target.Width=65;target.Margin=new Padding(12,3,0,0);count.Controls.Add(target);left.Controls.Add(count,0,5);
   loadExcluded.Text="기존 컨택 TXT 제외";loadExcluded.Dock=DockStyle.Fill;loadExcluded.Margin=new Padding(0,1,0,3);loadExcluded.Click+=(s,e)=>Import(true);left.Controls.Add(loadExcluded,0,6);
   excludedLabel.Text="제외 목록 0명";excludedLabel.AutoSize=true;excludedLabel.ForeColor=Muted;left.Controls.Add(excludedLabel,0,7);
   run.Text="▶  후보 찾기";run.Dock=DockStyle.Fill;run.BackColor=Ink;run.ForeColor=Color.White;run.Margin=new Padding(0,2,0,3);run.Font=new Font("맑은 고딕",11,FontStyle.Bold);run.Click+=async(s,e)=>await RunSearch();left.Controls.Add(run,0,8);
   cancel.Text="검색 중단";cancel.Enabled=false;cancel.Dock=DockStyle.Fill;cancel.Margin=new Padding(0,2,0,3);cancel.Click+=(s,e)=>{if(cts!=null)cts.Cancel();};left.Controls.Add(cancel,0,9);
   left.Controls.Add(new Label{Text="블로그 주소 1~10개 · 키워드는 쉼표로 구분",Dock=DockStyle.Fill,Font=new Font("맑은 고딕",8),ForeColor=Muted},0,10);
   var result=new CardPanel{Dock=DockStyle.Fill,Margin=new Padding(0)};body.Controls.Add(result,1,0);
   var resultLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));resultLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,35));resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));result.Controls.Add(resultLayout);
   var resultHeader=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};resultHeader.RowStyles.Add(new RowStyle(SizeType.Percent,100));resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,141));
   var badges=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};badges.Controls.Add(Caption("결과",10,true));resultCount.AutoSize=true;resultCount.BackColor=Ink;resultCount.ForeColor=Color.White;resultCount.Padding=new Padding(9,5,9,5);resultCount.Margin=new Padding(10,0,0,0);badges.Controls.Add(resultCount);
   selectionCount.AutoSize=true;selectionCount.BackColor=Color.FromArgb(225,245,233);selectionCount.ForeColor=Color.FromArgb(37,133,69);selectionCount.Padding=new Padding(9,5,9,5);selectionCount.Margin=new Padding(7,0,0,0);badges.Controls.Add(selectionCount);resultHeader.Controls.Add(badges,0,0);
   export.Text="TXT 저장";export.Dock=DockStyle.Fill;export.Margin=new Padding(8,0,0,6);export.BackColor=Orange;export.ForeColor=Color.White;export.Click+=(s,e)=>Export();resultHeader.Controls.Add(export,1,0);resultLayout.Controls.Add(resultHeader,0,0);
   var choices=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};var all=Small("전체 선택",()=>SelectAll(true));all.Height=27;all.BackColor=Color.White;choices.Controls.Add(all);var none=Small("선택 해제",()=>SelectAll(false));none.Height=27;none.BackColor=Color.White;choices.Controls.Add(none);choices.Controls.Add(new Label{Text="더블클릭으로 블로그 확인",AutoSize=true,ForeColor=Muted,Margin=new Padding(8,8,0,0),Font=new Font("맑은 고딕",8)});resultLayout.Controls.Add(choices,0,1);
   grid.Dock=DockStyle.Fill;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=38;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
   grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.GridColor=Color.FromArgb(235,235,243);
   grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.White,ForeColor=Muted,Font=new Font("맑은 고딕",8,FontStyle.Bold),Padding=new Padding(2,8,2,8)};
   grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.White,ForeColor=Ink,SelectionBackColor=Color.FromArgb(255,241,229),SelectionForeColor=Ink,Font=new Font("맑은 고딕",9),Padding=new Padding(3,10,3,10)};
   grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
   grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="select",HeaderText="",FillWeight=6,MinimumWidth=34});
   foreach(var def in new[]{new[]{"name","닉네임","18","90"},new[]{"url","URL","28","130"},new[]{"score","주제 겹침","12","72"},new[]{"latest","최근 글","16","90"},new[]{"evidence","선정 근거","35","170"}})grid.Columns.Add(new DataGridViewTextBoxColumn{Name=def[0],HeaderText=def[1],FillWeight=Int32.Parse(def[2]),MinimumWidth=Int32.Parse(def[3]),ReadOnly=true,SortMode=DataGridViewColumnSortMode.Automatic});
   grid.Columns["evidence"].DefaultCellStyle.WrapMode=DataGridViewTriState.True;
   grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.CellValueChanged+=(s,e)=>UpdateCounts();grid.RowsAdded+=(s,e)=>UpdateCounts();grid.RowsRemoved+=(s,e)=>UpdateCounts();
   grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Open(Convert.ToString(grid.Rows[e.RowIndex].Cells["url"].Value));};resultLayout.Controls.Add(grid,0,2);
   logPanel.Dock=DockStyle.Fill;logPanel.Visible=false;logPanel.Margin=new Padding(0,8,0,0);logs.Dock=DockStyle.Fill;logs.Multiline=true;logs.ReadOnly=true;logs.ScrollBars=ScrollBars.Vertical;logs.BackColor=Color.White;logs.BorderStyle=BorderStyle.FixedSingle;logPanel.Controls.Add(logs);main.Controls.Add(logPanel,0,2);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,116));status.Text="레퍼런스 입력 후 후보 찾기를 눌러 주세요.";status.Dock=DockStyle.Fill;status.ForeColor=Muted;status.Font=new Font("맑은 고딕",8);status.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(status,0,0);
   var toggle=new LinkLabel{Text="실행 로그 보기",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,LinkColor=Muted,ActiveLinkColor=Orange,VisitedLinkColor=Muted};toggle.LinkClicked+=(s,e)=>{logPanel.Visible=!logPanel.Visible;main.RowStyles[2].Height=logPanel.Visible?115:0;toggle.Text=logPanel.Visible?"실행 로그 닫기":"실행 로그 보기";};footer.Controls.Add(toggle,1,0);main.Controls.Add(footer,0,3);
   UpdateCounts();FormClosing+=(s,e)=>{if(busy){if(cts!=null)cts.Cancel();e.Cancel=true;Log("검색 중단 후 창을 닫아 주세요.");}};
  }
  void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){MessageBox.Show(e.Message,"열기 실패");}}
  void SelectAll(bool value){grid.EndEdit();foreach(DataGridViewRow r in grid.Rows)r.Cells[0].Value=value;}
  void Log(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}logs.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine);}
  void Import(bool exclusion){using(var d=new OpenFileDialog {Filter="텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*"})if(d.ShowDialog()==DialogResult.OK)try{var urls=Engine.ParseUrls(File.ReadAllText(d.FileName));if(urls.Count==0){MessageBox.Show("네이버 블로그 주소를 찾지 못했습니다.");return;}if(exclusion){excluded=new HashSet<string>(urls);excludedLabel.Text="제외 목록: "+excluded.Count+"개";}else refs.Text=String.Join(Environment.NewLine,urls);}catch(Exception e){MessageBox.Show(e.Message,"불러오기 실패");}}
  async Task RunSearch(){
   var urls=Engine.ParseUrls(refs.Text);if(urls.Count==0||urls.Count>10){MessageBox.Show("레퍼런스 네이버 블로그 주소를 1~10개 넣어 주세요.");return;}
   if((apiId.Text.Trim().Length>0)!=(apiSecret.Text.Trim().Length>0)){MessageBox.Show("공식 API를 쓰려면 Client ID와 Client Secret을 함께 입력해 주세요.");return;}
   busy=true;run.Enabled=false;cancel.Enabled=true;export.Enabled=false;loadRefs.Enabled=false;loadExcluded.Enabled=false;grid.Rows.Clear();logs.Clear();cts=new CancellationTokenSource();
   string kw=keywords.Text,id=apiId.Text.Trim(),secret=apiSecret.Text.Trim();int n=(int)target.Value;var exclusions=new HashSet<string>(excluded);status.Text="검색 중…";
   try {
    var report=await Task.Factory.StartNew(()=>Engine.Run(urls,exclusions,kw,n,id,secret,cts.Token,Log));
    foreach(var p in report.Profiles)grid.Rows.Add(false,p.Name,p.Url,p.Score,p.Latest,p.Evidence);
    foreach(string warning in report.Warnings)Log("확인사항: "+warning);
    status.Text="후보 "+report.Profiles.Count+"개 · 선택 후 TXT 저장 / 더블클릭으로 블로그 확인";
    if(report.Profiles.Count==0)Log("후보가 없습니다. 추가 키워드를 바꾸거나 검색 제한 메시지를 확인해 주세요.");
   }catch(Exception e){Log("검색 실패: "+e.Message);status.Text="검색 실패 · 아래 로그 확인";}
   finally{busy=false;run.Enabled=true;cancel.Enabled=false;export.Enabled=true;loadRefs.Enabled=true;loadExcluded.Enabled=true;cts.Dispose();cts=null;}
  }
  void Export(){grid.EndEdit();var urls=grid.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells[0].Value??false)).Select(r=>Convert.ToString(r.Cells["url"].Value)).Distinct().ToList();if(urls.Count==0){MessageBox.Show("저장할 후보를 선택해 주세요.");return;}using(var d=new SaveFileDialog {Filter="텍스트 파일 (*.txt)|*.txt",FileName="블로그주소_"+DateTime.Now.ToString("yyyyMMdd")+".txt"})if(d.ShowDialog()==DialogResult.OK)try{Engine.SaveAddresses(d.FileName,urls);Log("주소 "+urls.Count+"개 저장: "+d.FileName);}catch(Exception e){MessageBox.Show(e.Message,"저장 실패");}}
 }
 static class Program {
  static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
  static void Tests(string path,bool live) {
   var lines=new List<string>();
   try {
    Require(Engine.Normalize("https://m.blog.naver.com/PostView.naver?blogId=Baby0817&logNo=123")=="https://blog.naver.com/baby0817","게시글 쿼리 정규화");
    Require(Engine.Normalize("http://yummycook.blog.me/123")=="https://blog.naver.com/yummycook","blog.me 정규화");
    Require(Engine.Normalize("https://blog.naver.com.evil.test/abc")==null,"다른 도메인 거부");
    Require(Engine.Normalize("https://blog.naver.com/PostView.naver")==null,"아이디 없는 시스템 URL 제외");
    Require(Engine.ParseUrls("꼬마 / https://blog.naver.com/baby0817/123\nhttps://m.blog.naver.com/baby0817\nhttp://yummycook.blog.me/").Count==2,"TXT 추출 및 중복 제거");
    string exportPath=Path.ChangeExtension(path,"addresses.txt");
    Engine.SaveAddresses(exportPath,new[]{"https://blog.naver.com/baby0817/123","https://m.blog.naver.com/baby0817","http://yummycook.blog.me/"});
    var saved=File.ReadAllLines(exportPath);Require(saved.SequenceEqual(new[]{"https://blog.naver.com/baby0817","https://blog.naver.com/yummycook"}),"TXT 주소만 한 줄씩, 중복 및 헤더 없음");
    var bytes=File.ReadAllBytes(exportPath);Require(bytes[0]==239&&bytes[1]==187&&bytes[2]==191,"UTF-8 BOM 저장");
    var a=Engine.Terms("김장 배추김치 고춧가루 집밥");Require(Engine.Similarity(a,a)>0.999,"주제 겹침 동일 문서");Require(Engine.Similarity(a,Engine.Terms("게임 그래픽 모니터"))==0,"다른 주제 제외");
    string rss="<rss><channel><title>시험 블로그</title><item><title>배추김치 만드는법</title><pubDate>Tue, 06 Oct 2026 20:00:00 GMT</pubDate></item></channel></rss>";
    var p=Engine.ReadRss("test123",rss);Require(p.Latest=="2026-10-07"&&p.Titles.Count==1,"RSS 최근 날짜 서울 시간");
    var hits=Engine.ReadPublicSearch("<a href='https://blog.naver.com/test123/123'><b>배추김치</b></a><a href='https://m.blog.naver.com/test123/456'>다른글</a>","김장");Require(hits.Count==1&&hits[0].Id=="test123","공개 검색 링크 추출");
    using(var f=new MainForm()){f.CreateControl();Require(f.Controls.Count>0,"윈도우 폼 생성");}
    lines.Add("PASS: 주소 정규화, TXT 중복 제거, 도메인 검증, 주제 비교, RSS, 검색 파서, 폼 생성");
    if(live){var rp=Engine.FetchProfile("https://blog.naver.com/baby0817",CancellationToken.None,s=>lines.Add(s));Require(rp.Titles.Count>0,"실제 레퍼런스 글 조회");lines.Add("LIVE reference: "+rp.Name+" / "+rp.Latest);var found=Engine.Search("김장 배추김치","","",CancellationToken.None);Require(found.Count>0,"실제 웹 검색 후보 추출");lines.Add("LIVE search candidates: "+found.Count);}
   }catch(Exception e){lines.Add("FAIL: "+e);Environment.ExitCode=1;}
   File.WriteAllLines(path,lines,new UTF8Encoding(true));
  }
  [STAThread] static void Main(string[] args){
   ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length>=2&&args[0]=="--self-test"){Tests(args[1],false);return;}
   if(args.Length>=2&&args[0]=="--live-test"){Tests(args[1],true);return;}
   if(args.Length>=2&&args[0]=="--preview"){using(var f=new MainForm()){f.SavePreview(args[1]);}return;}
   if(args.Length>=2&&args[0]=="--pipeline-test") {
    try {
     using(var writer=new StreamWriter(args[1],false,new UTF8Encoding(true))) {
      writer.AutoFlush=true;
      var references=new List<string>{"https://blog.naver.com/baby0817"};var excluded=new HashSet<string>{"https://blog.naver.com/yummycook"};
      var report=Engine.Run(references,excluded,"김장 배추김치",10,"","",CancellationToken.None,s=>writer.WriteLine(s));
      Require(report.Profiles.Count>0,"실제 후보 추천 결과가 없습니다.");
      Require(report.Profiles.All(p=>!references.Contains(p.Url)&&!excluded.Contains(p.Url)&&p.Titles.Count>0&&p.Score>0),"레퍼런스/제외 목록/공개 글 검증 실패");
      File.WriteAllText(Path.ChangeExtension(args[1],"json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(true));
      writer.WriteLine("PASS: 레퍼런스 분석 → 검색 → 중복/기존 컨택 제외 → 최근 글 주제 비교 → 상위 "+report.Profiles.Count+"개");
     }
    }catch(Exception e){File.AppendAllText(args[1],"FAIL: "+e);Environment.ExitCode=1;}return;
   }
   Application.Run(new MainForm());
  }
 }
}
