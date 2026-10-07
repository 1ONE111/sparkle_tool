// 원고 검수기: 원고(.docx)의 수정 표시와 네이버 블로그 글을 대조해 검수 엑셀을 만듭니다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace WongoChecker {
 // ───────────────────────── 텍스트 도구
 public static class Tx {
  public const char RO='\u27E6', RC='\u27E7';   // 엑셀에서 빨간 글씨가 될 부분
  public static string Red(string s){ return String.IsNullOrEmpty(s)?"":RO+s+RC; }
  public static string Plain(string s){ return (s??"").Replace(RO.ToString(),"").Replace(RC.ToString(),""); }
  public static string N2(string s){ var sb=new StringBuilder(); foreach(char c in s??"") if(Char.IsLetterOrDigit(c)) sb.Append(Char.ToLowerInvariant(c)); return sb.ToString(); }
  public static string Clean(string s){ return Regex.Replace((s??"").Replace("\u200B","").Replace("\uFEFF",""),@"\s+"," ").Trim(); }
  public static string Cut(string s,int n){ s=Clean(s); return s.Length<=n?s:s.Substring(0,n)+"…"; }
  public static Dictionary<string,int> Bigrams(string a){ var d=new Dictionary<string,int>(); for(int i=0;i<a.Length-1;i++){ string k=a.Substring(i,2); int v; d.TryGetValue(k,out v); d[k]=v+1; } return d; }
  static int Inter(Dictionary<string,int> da,string b){ var d=new Dictionary<string,int>(da); int n=0; for(int i=0;i<b.Length-1;i++){ string k=b.Substring(i,2); int v; if(d.TryGetValue(k,out v)&&v>0){ n++; d[k]=v-1; } } return n; }
  public static double Dice(string a,string b){ if(a.Length<2||b.Length<2) return a==b?1:0; return 2.0*Inter(Bigrams(a),b)/(a.Length-1+b.Length-1); }
  // 블로그 쪽이 원고 문장보다 길어도(줄을 합쳐 쓴 경우) 1.5배까지는 감점하지 않는 유사도
  public static double Sim(Dictionary<string,int> da,int alen,string b){ if(alen<2||b.Length<2) return 0; double den=(alen-1)+Math.Min(b.Length-1,1.5*(alen-1)); return Math.Min(1,2.0*Inter(da,b)/den); }
  public static int Lev(string a,string b){ if(a.Length>200||b.Length>200) return 999; var d=new int[a.Length+1,b.Length+1]; for(int i=0;i<=a.Length;i++) d[i,0]=i; for(int j=0;j<=b.Length;j++) d[0,j]=j; for(int i=1;i<=a.Length;i++) for(int j=1;j<=b.Length;j++) d[i,j]=Math.Min(Math.Min(d[i-1,j]+1,d[i,j-1]+1),d[i-1,j-1]+(a[i-1]==b[j-1]?0:1)); return d[a.Length,b.Length]; }
  public static string[] Words(string s){ return Clean(s).Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries); }
  public static string LastWords(string s,int n){ var w=Words(s); return String.Join(" ",w.Skip(Math.Max(0,w.Length-n))); }
  public static string FirstWords(string s,int n){ return String.Join(" ",Words(s).Take(n)); }
  public static string J(params string[] xs){ return String.Join(" ",xs.Where(x=>!String.IsNullOrEmpty(x))); }
  public static string Circled(int i){ return i<20?((char)(0x2460+i)).ToString():"("+(i+1)+")"; }
  public static List<string> Sentences(string s){ return Regex.Split(s??"",@"(?<=[.!?~])\s+|\n").Select(Clean).Where(x=>x.Length>0).ToList(); }
  public static string Josa(string w,string batchim,string none){ if(String.IsNullOrEmpty(w)) return none; char c=w[w.Length-1]; if(c<0xAC00||c>0xD7A3) return none; return (c-0xAC00)%28>0?batchim:none; }
 }

 // ───────────────────────── 원고(.docx)
 public class Seg { public string Text; public char K; }   // k 본문, d 삭제 표시, f 수정 문구, m 메모, s 표 안 취소선(정가), r 빨간 글씨(판정 전)
 public class DPara {
  public List<Seg> Segs=new List<Seg>(); public bool InTable; public int Index;
  public string Of(params char[] ks){ var sb=new StringBuilder(); foreach(var s in Segs) if(ks.Contains(s.K)) sb.Append(s.Text); return sb.ToString(); }
  public string Exp { get { return Manuscript.StripPh(Of('k','f','s')); } }
  public bool Has(char k){ return Segs.Any(s=>s.K==k&&Tx.N2(s.Text).Length>0); }
 }
 public class DTable { public List<List<string>> Rows=new List<List<string>>(); public int StrikeCells; public string StrikeSample=""; }
 public class Manuscript {
  public string FilePath="", Name="", Title=""; public bool TitleExplicit; public int TitleIndex=-1;
  public List<DPara> Paras=new List<DPara>(); public List<DTable> Tables=new List<DTable>();
  public List<string> Links=new List<string>(), Placeholders=new List<string>(); public int Images;
  const string W="http://schemas.openxmlformats.org/wordprocessingml/2006/main", R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  public static readonly Regex Ph=new Regex(@"[\(\[<（【]\s*[^\(\)\[\]<>（）【】]{0,60}?(예정|자리|배치|첨부|삽입|넣어|들어갈)[^\(\)\[\]<>（）【】]{0,40}[\)\]>）】]");
  static readonly Regex NoteRx=new Regex(@"부탁|교체|삽입해|수정해|삭제해|확인하시어|번거롭|양해|죄송|자리|예정|첨부|가이드 내|넣어 ?주|반영해");
  public static string StripPh(string s){ return Ph.Replace(s??""," "); }
  public static string NameFromFile(string path){
   string b=System.IO.Path.GetFileNameWithoutExtension(path??"")??"";
   for(int i=0;i<3;i++) b=Regex.Replace(b,@"[\s_\-]*(\(\d+\)|검수본?|최종본?|수정본?|완료|v\d+)$","",RegexOptions.IgnoreCase).Trim();
   var parts=b.Split('_').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
   return parts.Length==0?b:parts[parts.Length-1];
  }
  static XmlDocument Xml(ZipArchiveEntry e){ var d=new XmlDocument{XmlResolver=null}; using(var s=e.Open()) d.Load(s); return d; }
  public static Manuscript Load(string path){
   var m=new Manuscript{FilePath=path,Name=NameFromFile(path)};
   XmlDocument doc; var rels=new Dictionary<string,string>();
   try {
    using(var z=ZipFile.OpenRead(path)){
     var e=z.GetEntry("word/document.xml"); if(e==null) throw new InvalidDataException("워드(.docx) 원고가 아니에요.");
     doc=Xml(e);
     var re=z.GetEntry("word/_rels/document.xml.rels");
     if(re!=null) foreach(XmlElement r in Xml(re).GetElementsByTagName("Relationship")) rels[r.GetAttribute("Id")]=r.GetAttribute("Target");
    }
   } catch(InvalidDataException){ throw; }
   catch(IOException ex){ throw new IOException("원고 파일을 열 수 없어요. 워드에서 열려 있으면 닫고 다시 해 주세요. ("+ex.Message+")"); }
   var ns=new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("w",W);
   var body=doc.SelectSingleNode("//w:body",ns); if(body==null) throw new InvalidDataException("원고 내용을 찾지 못했어요.");
   m.Walk(body,ns,rels);
   foreach(var p in m.Paras){ string t=Tx.Clean(p.Of('k','f','s')); if(t.StartsWith("※")&&NoteRx.IsMatch(t)) foreach(var s in p.Segs) if(s.K!='d') s.K='m'; }
   foreach(var p in m.Paras) foreach(Match x in Ph.Matches(p.Of('k','f','s'))) m.Placeholders.Add(Tx.Clean(x.Value));
   var first=m.Paras.Where(p=>!p.InTable&&Tx.N2(p.Exp).Length>0).Take(3).ToList();
   foreach(var p in first){ var t=Regex.Match(Tx.Clean(p.Exp),@"^(?:\[\s*제목\s*\]|제목\s*[:：])\s*(.+)$"); if(t.Success){ m.Title=t.Groups[1].Value.Trim(); m.TitleExplicit=true; m.TitleIndex=p.Index; break; } }
   if(m.TitleIndex<0&&first.Count>0){ var t=Regex.Match(Tx.Clean(first[0].Exp),@"^\[(.+)\]$"); m.Title=t.Success?t.Groups[1].Value.Trim():Tx.Clean(first[0].Exp); m.TitleExplicit=t.Success; if(t.Success) m.TitleIndex=first[0].Index; }
   return m;
  }
  void Walk(XmlNode parent,XmlNamespaceManager ns,Dictionary<string,string> rels){
   foreach(XmlNode c in parent.ChildNodes){
    if(c.LocalName=="p") Add(Para(c,false,ns,rels));
    else if(c.LocalName=="tbl"){
     var t=new DTable();
     foreach(XmlNode tr in c.SelectNodes("w:tr",ns)){
      var row=new List<string>();
      foreach(XmlNode tc in tr.SelectNodes("w:tc",ns)){
       var parts=new List<DPara>(); foreach(XmlNode p in tc.SelectNodes(".//w:p",ns)) parts.Add(Para(p,true,ns,rels));
       row.Add(String.Join(" ",parts.Select(x=>Tx.Clean(x.Exp)).Where(x=>x.Length>0)));
       if(parts.Any(x=>x.Has('s'))){ t.StrikeCells++; if(t.StrikeSample=="") t.StrikeSample=Tx.Clean(String.Concat(parts.SelectMany(x=>x.Segs).Where(s=>s.K=='s').Select(s=>s.Text))); }
       foreach(var x in parts) Add(x);
      }
      t.Rows.Add(row);
     }
     Tables.Add(t);
    }
    else if(c.LocalName=="sdt"){ var sc=c.SelectSingleNode("w:sdtContent",ns); if(sc!=null) Walk(sc,ns,rels); }
   }
  }
  void Add(DPara p){ p.Index=Paras.Count; Paras.Add(p); }
  static bool Under(XmlNode n,XmlNode stop,string local){ for(var x=n.ParentNode;x!=null&&x!=stop;x=x.ParentNode) if(x.LocalName==local) return true; return false; }
  static string Sym(string hex){ int v; if(!Int32.TryParse(hex,NumberStyles.HexNumber,null,out v)) return " "; v&=0xFF; return v==0xE0||v==0xE8?"→":v==0xDF?"←":v==0xE1?"↑":v==0xE2?"↓":" "; }
  static string Wing(string s){ return s.Replace('à','→').Replace('è','→').Replace('ß','←').Replace('á','↑').Replace('â','↓'); }
  static char Kind(XmlElement rPr,bool inTable,bool del,bool ins,XmlNamespaceManager ns){
   if(del) return 'd';
   bool strike=false; string hl="",col="",shd="";
   if(rPr!=null){
    foreach(XmlElement s in rPr.SelectNodes("w:strike|w:dstrike",ns)){ string v=s.GetAttribute("val",W); if(v!="0"&&v!="false"&&v!="off") strike=true; }
    var h=rPr.SelectSingleNode("w:highlight",ns) as XmlElement; if(h!=null) hl=h.GetAttribute("val",W);
    var c=rPr.SelectSingleNode("w:color",ns) as XmlElement; if(c!=null) col=c.GetAttribute("val",W).ToUpperInvariant();
    var sh=rPr.SelectSingleNode("w:shd",ns) as XmlElement; if(sh!=null) shd=sh.GetAttribute("fill",W).ToUpperInvariant();
   }
   bool red=col=="EE0000"||col=="FF0000";
   if(hl=="cyan"||shd=="00FFFF") return 'm';
   if(strike) return (red||!inTable)?'d':'s';
   if(red) return 'r';
   if(hl=="yellow"||shd=="FFFF00"||ins) return 'f';
   return 'k';
  }
  DPara Para(XmlNode p,bool inTable,XmlNamespaceManager ns,Dictionary<string,string> rels){
   var dp=new DPara{InTable=inTable};
   foreach(XmlNode r in p.SelectNodes(".//w:r",ns)){
    if(Under(r,p,"Fallback")) continue;
    bool del=Under(r,p,"del")||Under(r,p,"moveFrom"), ins=Under(r,p,"ins")||Under(r,p,"moveTo");
    var rPr=r.SelectSingleNode("w:rPr",ns) as XmlElement;
    bool wing=false;
    if(rPr!=null){ var f=rPr.SelectSingleNode("w:rFonts",ns) as XmlElement; if(f!=null) wing=(f.GetAttribute("ascii",W)+f.GetAttribute("hAnsi",W)+f.GetAttribute("cs",W)).IndexOf("Wingdings",StringComparison.OrdinalIgnoreCase)>=0; }
    var sb=new StringBuilder();
    foreach(XmlNode ch in r.ChildNodes){
     switch(ch.LocalName){
      case "t": case "delText": sb.Append(wing?Wing(ch.InnerText):ch.InnerText); break;
      case "tab": case "br": case "cr": sb.Append(' '); break;
      case "noBreakHyphen": sb.Append('-'); break;
      case "sym": sb.Append(Sym(((XmlElement)ch).GetAttribute("char",W))); break;
      case "drawing": case "pict": case "object": Images++; break;
     }
    }
    if(sb.Length==0) continue;
    dp.Segs.Add(new Seg{Text=sb.ToString(),K=Kind(rPr,inTable,del,ins,ns)});
   }
   // 빨간 글씨는 검수 메모처럼 보이면 메모, 아니면 본문
   string red=String.Concat(dp.Segs.Where(s=>s.K=='r').Select(s=>s.Text)).Trim();
   bool note=red.Length>0&&(NoteRx.IsMatch(red)||Regex.IsMatch(red,@"^[※<\(\[]"));
   foreach(var s in dp.Segs) if(s.K=='r') s.K=note?'m':'k';
   foreach(XmlElement h in p.SelectNodes(".//w:hyperlink",ns)){
    string id=h.GetAttribute("id",R), target;
    if(id.Length==0||!rels.TryGetValue(id,out target)||!target.StartsWith("http",StringComparison.OrdinalIgnoreCase)) continue;
    bool live=false;
    foreach(XmlNode r in h.SelectNodes(".//w:r",ns)){ char k=Kind(r.SelectSingleNode("w:rPr",ns) as XmlElement,inTable,Under(r,p,"del"),false,ns); if(k!='d'&&k!='m') live=true; }
    if(live) Links.Add(target);
   }
   return dp;
  }
 }

 // ───────────────────────── 블로그 글
 public class PLink { public string Kind="", Href="", Text=""; }
 public class Post {
  public string Url="", BlogId="", LogNo="", Title="", Date="", Nick="";
  public List<string> Lines=new List<string>(), Extra=new List<string>();
  public List<List<List<string>>> Tables=new List<List<List<string>>>(); public List<int> TableStrikes=new List<int>();
  public List<PLink> Links=new List<PLink>(); public List<string> Tags, AllTags=new List<string>(); public int Images;
  public string AllN2="";
  const RegexOptions S=RegexOptions.Singleline|RegexOptions.IgnoreCase;
  const string UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
  public static string Get(string url,out Uri final){
   var req=(HttpWebRequest)WebRequest.Create(url); req.Timeout=20000; req.ReadWriteTimeout=20000; req.UserAgent=UA;
   req.Headers["Accept-Language"]="ko-KR,ko;q=0.9"; req.Referer="https://blog.naver.com/"; req.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   using(var r=(HttpWebResponse)req.GetResponse()) using(var s=r.GetResponseStream()) using(var sr=new StreamReader(s,Encoding.UTF8)){ final=r.ResponseUri; return sr.ReadToEnd(); }
  }
  public static bool Ids(string url,out string id,out string no){
   id=no=null; url=WebUtility.HtmlDecode(url??"");
   var a=Regex.Match(url,@"blogId=([A-Za-z0-9_-]+)",RegexOptions.IgnoreCase); var b=Regex.Match(url,@"logNo=(\d+)",RegexOptions.IgnoreCase);
   if(a.Success&&b.Success){ id=a.Groups[1].Value; no=b.Groups[1].Value; return true; }
   var c=Regex.Match(url,@"blog\.naver\.com/([A-Za-z0-9_-]+)/(\d+)",RegexOptions.IgnoreCase);
   if(c.Success){ id=c.Groups[1].Value; no=c.Groups[2].Value; return true; }
   return false;
  }
  static string Rx(string s,string pattern){ var m=Regex.Match(s??"",pattern,S); return m.Success?m.Groups[1].Value:""; }
  static string Meta(string html,string prop){ return WebUtility.HtmlDecode(Rx(html,"<meta[^>]+property=\""+Regex.Escape(prop)+"\"[^>]+content=\"([^\"]*)\"")); }
  public static string Strip(string s){
   s=Regex.Replace(s??"","<!--.*?-->","",S); s=Regex.Replace(s,@"<br\s*/?>"," ",S); s=Regex.Replace(s,"<[^>]+>","");
   return Tx.Clean(WebUtility.HtmlDecode(s));
  }
  public static Post Fetch(string url){
   string id,no; Uri fin; url=Tx.Clean(url);
   if(!Ids(url,out id,out no)){
    string u=url.StartsWith("http",StringComparison.OrdinalIgnoreCase)?url:"https://"+url; Uri tmp;
    if(!Uri.TryCreate(u,UriKind.Absolute,out tmp)) throw new ArgumentException("링크 형식이 아니에요.");
    string h=Get(u,out fin);
    if(!Ids(fin.ToString(),out id,out no)&&!Ids(Meta(h,"og:url"),out id,out no)) throw new ArgumentException("네이버 블로그 글 주소가 아니에요.");
   }
   var p=Parse(Get("https://blog.naver.com/PostView.naver?blogId="+id+"&logNo="+no,out fin));
   p.BlogId=id; p.LogNo=no; p.Url="https://blog.naver.com/"+id+"/"+no; p.Tags=FetchTags(id,no); p.Finish(); return p;
  }
  public static List<string> FetchTags(string id,string no){
   try {
    Uri f; string j=Get("https://blog.naver.com/BlogTagListInfo.naver?blogId="+id+"&logNoList="+no+"&logType=mylog",out f);
    var m=Regex.Match(j,"\"tagName\"\\s*:\\s*\"([^\"]*)\""); if(!m.Success) return null;
    return Uri.UnescapeDataString(m.Groups[1].Value.Replace("+"," ")).Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
   } catch { return null; }
  }
  public static Post Parse(string html){
   var p=new Post();
   p.Title=Strip(Rx(html,"se-title-text\"[^>]*>(.*?)</div>")); if(p.Title=="") p.Title=Meta(html,"og:title");
   p.Date=Tx.Clean(Rx(html,"se_publishDate[^>]*>([^<]+)<")); p.Nick=Rx(html,"nickName\\s*=\\s*'([^']*)'");
   string id,no; if(Ids(Meta(html,"og:url"),out id,out no)){ p.BlogId=id; p.LogNo=no; p.Url="https://blog.naver.com/"+id+"/"+no; }
   int s=html.IndexOf("<div class=\"se-main-container\"");
   if(s>=0){
    int e=html.IndexOf("id=\"post_footer_contents\"",s); string body=html.Substring(s,(e<0?html.Length:e)-s);
    foreach(string c in Regex.Split(body,"(?=<div class=\"se-component se-)")){ var t=Regex.Match(c,"^<div class=\"se-component se-(\\w+)"); if(t.Success) p.Component(t.Groups[1].Value,c); }
   } else {
    int a=html.IndexOf("id=\"postViewArea\"");
    if(a<0) throw new InvalidDataException("글 내용을 읽지 못했어요. 삭제·비공개 글이거나 주소가 잘못됐어요.");
    string body=html.Substring(a); int e=body.IndexOf("id=\"post_footer_contents\""); if(e>0) body=body.Substring(0,e);
    foreach(Match l in Regex.Matches(body,"<a [^>]*href=\"([^\"#][^\"]*)\"[^>]*>(.*?)</a>",S)) p.Links.Add(new PLink{Kind="글자 링크",Href=WebUtility.HtmlDecode(l.Groups[1].Value),Text=Strip(l.Groups[2].Value)});
    foreach(string ln in Regex.Split(Regex.Replace(body,@"<(br|/p|/div|/li|/h\d)[^>]*>","\n",S),"\n")){ string t=Strip(ln); if(t.Length>0) p.Lines.Add(t); }
    p.Images=Regex.Matches(body,"<img ",S).Count;
   }
   return p;
  }
  List<string> Paras(string chunk){
   var res=new List<string>();
   foreach(Match m in Regex.Matches(chunk,"<p class=\"se-text-paragraph[^\"]*\"[^>]*>(.*?)</p>",S)){
    string inner=m.Groups[1].Value;
    foreach(Match a in Regex.Matches(inner,"<a [^>]*href=\"([^\"#][^\"]*)\"[^>]*>(.*?)</a>",S)) Links.Add(new PLink{Kind="글자 링크",Href=WebUtility.HtmlDecode(a.Groups[1].Value),Text=Strip(a.Groups[2].Value)});
    string t=Strip(inner); if(t.Length>0) res.Add(t);
   }
   return res;
  }
  void Component(string type,string c){
   switch(type){
    case "documentTitle": break;
    case "image": case "imageGroup": case "imageStrip": case "imageSlide":
     Images+=Math.Max(1,Regex.Matches(c,"se-image-resource").Count);
     foreach(Match l in Regex.Matches(c,"\"linkUse\"\\s*:\\s*\"true\"\\s*,\\s*\"link\"\\s*:\\s*\"([^\"]+)\"")) Links.Add(new PLink{Kind="이미지 링크",Href=WebUtility.HtmlDecode(l.Groups[1].Value)});
     Lines.AddRange(Paras(c)); break;
    case "table":
     var rows=new List<List<string>>(); int strikes=0;
     foreach(Match tr in Regex.Matches(c,"<tr.*?</tr>",S)){
      var row=new List<string>();
      foreach(Match td in Regex.Matches(tr.Value,"<td.*?</td>",S)){ row.Add(String.Join(" ",Paras(td.Value))); if(Regex.IsMatch(td.Value,"<strike>|<s>|<del>|line-through",RegexOptions.IgnoreCase)) strikes++; }
      rows.Add(row);
     }
     Tables.Add(rows); TableStrikes.Add(strikes); break;
    case "oglink":
     string t=Strip(Rx(c,"<strong class=\"se-oglink-title\">(.*?)</strong>"));
     Links.Add(new PLink{Kind="링크 카드",Href=WebUtility.HtmlDecode(Rx(c,"data-linkdata='\\{[^']*\"link\"\\s*:\\s*\"([^\"]+)\"")),Text=t});
     Extra.Add(t); Extra.Add(Strip(Rx(c,"<p class=\"se-oglink-summary\">(.*?)</p>"))); break;
    case "oembed":
     string dec=WebUtility.HtmlDecode(c);
     Links.Add(new PLink{Kind="영상",Href=Rx(dec,"\"inputUrl\"\\s*:\\s*\"([^\"]+)\"")}); Extra.Add(Rx(dec,"\"title\"\\s*:\\s*\"([^\"]+)\"")); break;
    default: Lines.AddRange(Paras(c)); break;
   }
  }
  public void Finish(){
   var all=new List<string>{Title}; all.AddRange(Lines); foreach(var t in Tables) foreach(var r in t) all.AddRange(r); all.AddRange(Extra);
   AllN2=Tx.N2(String.Join(" ",all));
   var tags=new List<string>(Tags??new List<string>());
   foreach(var l in Lines) foreach(Match m in Regex.Matches(l,@"#([^\s#,]+)")) tags.Add(m.Groups[1].Value.TrimEnd('.','!','?',')'));
   AllTags=tags.Where(x=>Checker.TagKey(x).Length>0).GroupBy(Checker.TagKey).Select(g=>g.First()).ToList();
  }
 }

 // ───────────────────────── 대조
 public class Result {
  public List<string> Fix=new List<string>(), Notes=new List<string>();
  public string Summary(){ if(Fix.Count==0) return "이상 없음"; if(Fix.Count==1) return Fix[0]; return String.Join("\n",Fix.Select((x,i)=>Tx.Circled(i)+" "+x)); }
 }
 public class Hunk {
  public string[] A,B; public int A0,A1,B0,B1;
  public string APart { get { return String.Join(" ",A,A0,A1-A0); } }
  public string BPart { get { return String.Join(" ",B,B0,B1-B0); } }
 }
 public static class Checker {
  static readonly Regex UrlRx=new Regex(@"https?://[^\s<>""'|]+",RegexOptions.IgnoreCase), TagRx=new Regex(@"#([^\s#,]+)");
  static readonly Regex Space=new Regex(@"^(%20|%C2%A0|%E2%80%8B|%EF%BB%BF|%09|\s)+|(%20|%C2%A0|%E2%80%8B|%EF%BB%BF|%09|\s)+$",RegexOptions.IgnoreCase);
  static readonly Regex DiscRx=new Regex("지원을 받|지원받|원고료|협찬|제공받|소정의|광고");
  static readonly HashSet<string> DupOk=new HashSet<string>("너무 정말 진짜 아주 많이 빨리 천천히 하나 계속 자꾸 다시 조금 살짝 반짝 차근 하하 호호 헤헤 히히 두근 콩닥 엉엉 냠냠 쑥쑥 쭉쭉".Split(' '));
  public static Result Compare(Manuscript m,Post p){ var c=new Ctx(m,p); c.Run(); return c.R; }
  public static string TagKey(string t){ return Regex.Replace((t??"").ToLowerInvariant(),@"[\s#]",""); }
  public static string UrlKey(string u){
   u=Space.Replace(WebUtility.HtmlDecode(u??"").Trim(),"");
   u=Regex.Replace(u,@"^[a-z]+://","",RegexOptions.IgnoreCase); if(u.StartsWith("www.",StringComparison.OrdinalIgnoreCase)) u=u.Substring(4);
   var yt=Regex.Match(u,@"(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/)|youtu\.be/)([A-Za-z0-9_-]{6,})",RegexOptions.IgnoreCase); if(yt.Success) return "youtube:"+yt.Groups[1].Value;
   u=u.TrimEnd('/'); int sl=u.IndexOf('/'); return sl<0?u.ToLowerInvariant():u.Substring(0,sl).ToLowerInvariant()+u.Substring(sl);
  }
  static string Host(string key){ int i=key.IndexOf('/'); return i<0?key:key.Substring(0,i); }
  public static string TrimUrl(string u){ return (u??"").TrimEnd('.',',',')',']','}','>','!','?','…'); }
  public static List<Hunk> Hunks(string post,string doc){
   var a=Tx.Words(post); var b=Tx.Words(doc); var ka=a.Select(Tx.N2).ToArray(); var kb=b.Select(Tx.N2).ToArray();
   int n=a.Length,mm=b.Length; var res=new List<Hunk>(); if((long)n*mm>400000) return res;
   var L=new int[n+1,mm+1];
   for(int i=n-1;i>=0;i--) for(int j=mm-1;j>=0;j--) L[i,j]=ka[i]==kb[j]?L[i+1,j+1]+1:Math.Max(L[i+1,j],L[i,j+1]);
   int x=0,y=0; Hunk cur=null;
   while(x<n||y<mm){
    if(x<n&&y<mm&&ka[x]==kb[y]){ if(cur!=null){ cur.A1=x; cur.B1=y; res.Add(cur); cur=null; } x++; y++; }
    else { if(cur==null) cur=new Hunk{A=a,B=b,A0=x,B0=y}; if(y>=mm||(x<n&&L[x+1,y]>=L[x,y+1])) x++; else y++; }
   }
   if(cur!=null){ cur.A1=n; cur.B1=mm; res.Add(cur); }
   // 블로그 쪽 앞뒤로 붙은 다른 문장, 띄어쓰기만 다른 부분은 뺀다
   return res.Where(h=>!(h.B0==h.B1&&(h.B0==0||h.B0==mm))&&Tx.N2(h.APart)!=Tx.N2(h.BPart)).ToList();
  }
  public static string Snip(Hunk h){
   int small=Tx.N2(h.APart).Length+Tx.N2(h.BPart).Length, cb=small<8?2:1;
   int a0=Math.Max(0,h.A0-cb), b0=Math.Max(0,h.B0-cb);
   string aB=String.Join(" ",h.A,a0,h.A0-a0), aA=String.Join(" ",h.A.Skip(h.A1).Take(1)), bB=String.Join(" ",h.B,b0,h.B0-b0), bA=String.Join(" ",h.B.Skip(h.B1).Take(1));
   return "'"+Tx.J(aB,Tx.Red(Tx.Cut(h.APart,40)),aA)+"' → '"+Tx.J(bB,Tx.Cut(h.BPart,40),bA)+"'";
  }
  public static string MarkUrl(string post,string doc){
   int p=0; while(p<post.Length&&p<doc.Length&&post[p]==doc[p]) p++;
   int s=0; while(s<post.Length-p&&s<doc.Length-p&&post[post.Length-1-s]==doc[doc.Length-1-s]) s++;
   if(post.Length-p-s<=0) return post; return post.Substring(0,p)+Tx.Red(post.Substring(p,post.Length-p-s))+post.Substring(post.Length-s);
  }

  class Ctx {
   public Result R=new Result(); Manuscript m; Post p; int titleIdx;
   List<string> wins=new List<string>(), winN=new List<string>();
   List<string> fTitle=new List<string>(), fBody=new List<string>(), fLink=new List<string>(), fTag=new List<string>(), fBroken=new List<string>(), fTable=new List<string>();
   HashSet<int> done=new HashSet<int>();
   public Ctx(Manuscript m,Post p){
    this.m=m; this.p=p; titleIdx=m.TitleIndex;
    if(titleIdx<0){ var f=m.Paras.FirstOrDefault(x=>!x.InTable&&Tx.N2(x.Exp).Length>0); if(f!=null&&Tx.N2(f.Exp)==Tx.N2(p.Title)) titleIdx=f.Index; }
    for(int i=0;i<p.Lines.Count;i++) for(int k=1;k<=3&&i+k<=p.Lines.Count;k++){ string t=String.Join(" ",p.Lines.Skip(i).Take(k)); wins.Add(t); winN.Add(Tx.N2(t)); }
    foreach(var t in p.Tables) foreach(var row in t){ string s=String.Join(" ",row); wins.Add(s); winN.Add(Tx.N2(s)); }
   }
   void Note(string s){ if(R.Notes.Count<40&&!R.Notes.Contains(s)) R.Notes.Add(s); }
   bool InPost(string text){ string n=Tx.N2(text); return n.Length>0&&p.AllN2.Contains(n); }
   string Best(string doc,out double score){
    string dn=Tx.N2(doc); var bg=Tx.Bigrams(dn); score=0; string best="";
    for(int i=0;i<wins.Count;i++){ double s=Tx.Sim(bg,dn.Length,winN[i]); if(s>score+1e-9){ score=s; best=wins[i]; } }
    return best;
   }
   public void Run(){
    Title(); Disclosure(); Edits(); Body(); Dups(); Memos(); Links(); Tags(); Broken(); Tables();
    foreach(var l in new[]{fTitle,fBody,fLink,fTag,fBroken,fTable}) foreach(var x in l) if(!R.Fix.Contains(x)) R.Fix.Add(x);
   }
   void Title(){
    if(!m.TitleExplicit||m.Title.Length==0||p.Title.Length==0||Tx.N2(m.Title)==Tx.N2(p.Title)) return;
    var h=Tx.Dice(Tx.N2(m.Title),Tx.N2(p.Title))>=0.5?Hunks(p.Title,m.Title):new List<Hunk>();
    if(h.Count>0) fTitle.Add("제목 다름: "+Snip(h[0])); else Note("제목이 원고와 다름: '"+Tx.Cut(p.Title,50)+"'");
   }
   // 광고 고지 문구는 보통 이미지로 넣으니 비교하지 않는다
   void Disclosure(){
    var disc=m.Paras.Where(x=>!x.InTable&&x.Index!=titleIdx&&Tx.N2(x.Exp).Length>0).Take(4).FirstOrDefault(x=>DiscRx.IsMatch(x.Exp));
    if(disc!=null) done.Add(disc.Index);
   }
   static bool WholeDel(DPara d){ return d.Has('d')&&Tx.N2(d.Exp).Length==0; }
   static double FixRatio(DPara d){ int all=Tx.N2(d.Exp).Length; return all==0?0:(double)d.Segs.Where(s=>s.K=='f').Sum(s=>Tx.N2(s.Text).Length)/all; }
   DPara Neighbor(int from,int step){ for(int i=from;i>=0&&i<m.Paras.Count;i+=step){ var x=m.Paras[i]; if(Tx.N2(x.Exp+x.Of('d')).Length>0) return x; } return null; }
   void Edits(){
    var ps=m.Paras; int i=0;
    // 1) 문단째 지운 표시가 이어진 구간 먼저 (바로 앞뒤의 노란 문단이 대신 들어갈 문장)
    while(i<ps.Count){
     if(i==titleIdx||!WholeDel(ps[i])){ i++; continue; }
     int j=i; var left=new List<DPara>();
     while(j<ps.Count&&(WholeDel(ps[j])||Tx.N2(ps[j].Exp+ps[j].Of('d')).Length==0)){ string dn=Tx.N2(ps[j].Of('d')); if(WholeDel(ps[j])&&dn.Length>=6&&p.AllN2.Contains(dn)) left.Add(ps[j]); done.Add(j); j++; }
     if(left.Count>0){
      DPara rep=null; foreach(var nb in new[]{Neighbor(j,1),Neighbor(i-1,-1)}) if(rep==null&&nb!=null&&!WholeDel(nb)&&FixRatio(nb)>=0.6&&!InPost(nb.Exp)) rep=nb;
      string first=Tx.Red(Tx.Cut(left[0].Of('d'),60)), more=left.Count>1?" 외 "+(left.Count-1)+"문단":"";
      if(rep!=null){ fBody.Add("수정 미반영: '"+first+"'"+more+" → '"+Tx.Cut(rep.Exp,60)+"'"); done.Add(rep.Index); }
      else fBody.Add("삭제 미반영: '"+first+"'"+more+" → 문단 삭제");
     }
     i=j;
    }
    // 2) 문장 안의 삭제 표시와 노란 수정 문구
    foreach(var pa in ps){
     if(pa.Index==titleIdx||done.Contains(pa.Index)) continue;
     if(pa.Has('d')) PartialDel(pa);
     if(pa.Has('f')&&!done.Contains(pa.Index)) FixCheck(pa);
    }
   }
   void PartialDel(DPara pa){
    var segs=pa.Segs.Where(s=>s.K!='m').ToList();
    for(int k=0;k<segs.Count;k++){
     if(segs[k].K!='d') continue;
     int e=k; var sb=new StringBuilder(); while(e<segs.Count&&segs[e].K=='d'){ sb.Append(segs[e].Text); e++; }
     string D=Tx.Clean(sb.ToString()), dn=Tx.N2(D); int start=k; k=e-1;
     if(dn.Length==0) continue;
     string before=Manuscript.StripPh(String.Concat(segs.Take(start).Where(s=>s.K!='d'&&s.K!='f').Select(s=>s.Text)));
     string after=Manuscript.StripPh(String.Concat(segs.Skip(e).Where(s=>s.K!='d'&&s.K!='f').Select(s=>s.Text)));
     string bw=Tx.LastWords(before,1), aw=Tx.FirstWords(after,2), probe=Tx.N2(bw+D+aw);
     bool left=probe.Length>dn.Length?p.AllN2.Contains(probe):dn.Length>=6&&p.AllN2.Contains(dn);
     if(!left&&dn.Length>=15&&p.AllN2.Contains(dn)) left=true;
     if(!left) continue;
     string F=""; int f=e; while(f<segs.Count&&segs[f].K=='f'){ F+=segs[f].Text; f++; }
     if(F=="") for(int g=start-1;g>=0&&segs[g].K=='f';g--) F=segs[g].Text+F;
     F=Tx.Clean(Manuscript.StripPh(F));
     string shown="'"+Tx.J(bw,Tx.Red(Tx.Cut(D,50)),aw)+"'";
     if(F.Length>0) fBody.Add("수정 미반영: "+shown+" → '"+Tx.J(bw,F,aw)+"'");
     else fBody.Add("삭제 미반영: "+shown+" → '"+Tx.Cut(D,50)+"' 삭제");
     done.Add(pa.Index);
    }
   }
   void FixCheck(DPara pa){
    string exp=Tx.Clean(pa.Exp); if(Tx.N2(exp).Length==0||InPost(exp)) return;
    var segs=pa.Segs.Where(s=>s.K!='m'&&s.K!='d').ToList();
    for(int k=0;k<segs.Count;k++){
     if(segs[k].K!='f') continue;
     int e=k; var sb=new StringBuilder(); while(e<segs.Count&&segs[e].K=='f'){ sb.Append(segs[e].Text); e++; }
     int start=k; k=e-1;
     string F=Tx.Clean(Manuscript.StripPh(sb.ToString())), fn=Tx.N2(F);
     if(fn.Length<2||Regex.IsMatch(F,@"^https?://\S+$",RegexOptions.IgnoreCase)) continue;
     string before=Manuscript.StripPh(String.Concat(segs.Take(start).Select(s=>s.Text))), after=Manuscript.StripPh(String.Concat(segs.Skip(e).Select(s=>s.Text)));
     if(p.AllN2.Contains(Tx.N2(Tx.LastWords(before,1)+F+Tx.FirstWords(after,1)))||(fn.Length>=12&&p.AllN2.Contains(fn))) continue;
     string sent=Tx.Sentences(exp).FirstOrDefault(x=>Tx.N2(x).Contains(fn))??exp; double sc; string best=Best(sent,out sc);
     done.Add(pa.Index);
     if(sc>=0.5){
      var hs=Hunks(best,sent); var h=hs.FirstOrDefault(x=>{ string bn=Tx.N2(x.BPart); return bn.Length>0&&(fn.Contains(bn)||bn.Contains(fn)); })??hs.FirstOrDefault();
      if(h!=null){ bool typo=Tx.N2(h.APart).Length>0&&Tx.Lev(Tx.N2(h.APart),Tx.N2(h.BPart))<=2; fBody.Add((typo?"오타 수정 미반영: ":"수정 미반영: ")+Snip(h)); return; }
     }
     Note("수정 문구를 블로그에서 못 찾음: '"+Tx.Cut(F,40)+"'"); return;
    }
   }
   void Body(){
    foreach(var pa in m.Paras){
     if(pa.InTable||pa.Index==titleIdx||done.Contains(pa.Index)) continue;
     string exp=Tx.Clean(pa.Exp); if(Tx.N2(exp).Length<6||InPost(exp)||Skip(exp)) continue;
     foreach(var s in Tx.Sentences(exp)){
      string sn=Tx.N2(s); if(sn.Length<6||p.AllN2.Contains(sn)||Skip(s)) continue;
      double sc; string best=Best(s,out sc);
      if(sc<0.6){ Note("원고 문장이 블로그에 없음 (사진으로 넣었는지 확인): '"+Tx.Cut(s,45)+"'"); continue; }
      var hs=Hunks(best,s).Where(h=>!(h.BPart.Length==0&&best.Contains(h.APart+" "+h.APart))).ToList(); if(hs.Count==0) continue;
      var num=hs.FirstOrDefault(h=>Regex.IsMatch(h.APart,@"\d")&&Regex.IsMatch(h.BPart,@"\d"));
      if(num!=null&&sc>=0.75) fBody.Add("숫자 다름: "+Snip(num));
      else if(hs.Count<=2&&hs.All(h=>Tx.N2(h.APart).Length+Tx.N2(h.BPart).Length<=12)) Note("오타 의심: "+Snip(hs[0]));
      else Note("문장이 원고와 다름: '"+Tx.Cut(s,45)+"'");
     }
    }
   }
   // 해시태그 줄, 주소만 있는 줄은 문장 비교에서 뺀다
   static bool Skip(string s){ var w=Tx.Words(s); return w.Length==0||w.Count(x=>x.StartsWith("#"))*2>=w.Length||Regex.IsMatch(s,@"^(https?://)?[\w.-]+\.(com|co\.kr|kr|net|org|me)(/\S*)?$",RegexOptions.IgnoreCase); }
   void Dups(){
    string docAll=String.Join(" ",m.Paras.Select(x=>x.Of('k','f','s')));
    foreach(var l in p.Lines) foreach(Match d in Regex.Matches(l,@"(?<![가-힣A-Za-z])([가-힣A-Za-z]{2,})\s+\1(?![가-힣A-Za-z])")){
     string w=d.Groups[1].Value; if(DupOk.Contains(w)) continue;
     string bw=Tx.LastWords(l.Substring(0,d.Index),1), aw=Tx.FirstWords(l.Substring(d.Index+d.Length),2);
     string msg="'"+Tx.J(bw,w,Tx.Red(w),aw)+"' → '"+w+"' 중복 삭제";
     if(Regex.IsMatch(docAll,Regex.Escape(w)+@"\s+"+Regex.Escape(w))) Note("원고에도 있는 중복 단어: "+msg); else fBody.Add("오타: "+msg);
    }
   }
   void Memos(){
    var seen=new HashSet<string>();
    foreach(var pa in m.Paras) foreach(var s in Tx.Sentences(pa.Of('m'))){
     string n=Tx.N2(s); if(n.Length>=8&&seen.Add(n)&&p.AllN2.Contains(n)) fBody.Add("검수 메모가 블로그에 그대로 남아 있음: '"+Tx.Red(Tx.Cut(s,40))+"' → 삭제");
    }
    foreach(var ph in m.Placeholders.Distinct()){ string n=Tx.N2(ph); if(n.Length>=4&&p.AllN2.Contains(n)) fBody.Add("자리표시 문구가 남아 있음: '"+Tx.Red(ph)+"' → 삭제"); }
   }
   void Links(){
    var docUrls=new List<string>();
    foreach(var pa in m.Paras) foreach(Match u in UrlRx.Matches(pa.Of('k','f','s'))) docUrls.Add(TrimUrl(u.Value));
    docUrls.AddRange(m.Links);
    foreach(var l in p.Links.Where(x=>Space.IsMatch(x.Href??""))){
     string k=UrlKey(l.Href); var other=p.Links.FirstOrDefault(o=>o!=l&&!Space.IsMatch(o.Href??"")&&UrlKey(o.Href)==k);
     string label=l.Text.Length>0?l.Text:Space.Replace(l.Href,"");
     fLink.Add("'"+Tx.Cut(label,50)+"' "+l.Kind+" 주소 끝에 공백 문자가 붙어 클릭 시 오류 → 링크 다시 걸기"+(other!=null?" ("+other.Kind+Tx.Josa(other.Kind,"은","는")+" 정상)":""));
    }
    var postUrls=p.Links.Select(l=>l.Href).ToList();
    foreach(var line in p.Lines.Concat(p.Extra)) foreach(Match u in UrlRx.Matches(line??"")) postUrls.Add(TrimUrl(u.Value));
    var keys=new HashSet<string>(postUrls.Select(UrlKey)); var seen=new HashSet<string>();
    foreach(var du in docUrls){
     string k=UrlKey(du); if(k.Length<4||!seen.Add(k)||keys.Contains(k)) continue;
     string doc=Space.Replace(WebUtility.HtmlDecode(du).Trim(),"");
     var same=postUrls.Select(x=>Space.Replace(WebUtility.HtmlDecode(x??"").Trim(),"")).Where(x=>x.Length>0&&Host(UrlKey(x))==Host(k)).Distinct().ToList();
     if(same.Count>0){ string pu=same.OrderByDescending(x=>Tx.Dice(x,doc)).First(); fLink.Add("링크 주소 다름: '"+MarkUrl(pu,doc)+"' → '"+doc+"'"); }
     else fLink.Add("링크 누락: '"+doc+"'");
    }
   }
   void Tags(){
    var doc=new List<string>();
    foreach(var pa in m.Paras) foreach(Match t in TagRx.Matches(pa.Of('k','f','s'))) doc.Add(t.Groups[1].Value.TrimEnd('.','!','?',')'));
    doc=doc.Where(x=>TagKey(x).Length>0).GroupBy(TagKey).Select(g=>g.First()).ToList();
    if(doc.Count==0) return;
    if(p.AllTags.Count==0){ if(p.Tags==null) Note("블로그 태그를 불러오지 못했어요 (태그는 직접 확인)"); else fTag.Add("태그 없음 (원고 태그 "+doc.Count+"개)"); return; }
    var pk=new HashSet<string>(p.AllTags.Select(TagKey)); var dk=new HashSet<string>(doc.Select(TagKey));
    var extra=p.AllTags.Where(t=>!dk.Contains(TagKey(t))).ToList();
    foreach(var d in doc.Where(x=>!pk.Contains(TagKey(x)))){
     var best=extra.OrderByDescending(x=>Tx.Dice(TagKey(x),TagKey(d))).FirstOrDefault();
     if(best!=null&&Tx.Dice(TagKey(best),TagKey(d))>=0.5){ fTag.Add("태그 오타: '"+Tx.Red("#"+best)+"' → '#"+d+"'"); extra.Remove(best); }
     else fTag.Add("태그 누락: '#"+d+"'");
    }
    if(extra.Count>0) Note("원고에 없는 태그: "+String.Join(" ",extra.Select(x=>"#"+x)));
   }
   static bool IsBroken(char c){ return (c>='\u00C0'&&c<='\u00FF'&&c!='×'&&c!='÷')||c=='\uFFFD'||(c>='\uF000'&&c<='\uF0FF'); }
   string TableLabel(){ return m.Tables.Any(t=>t.Rows.Any(r=>r.Any(c=>c.Contains("요금"))))?"요금제 표":"표"; }
   void Broken(){
    string docAll=String.Concat(m.Paras.Select(x=>x.Of('k','f','s','d','m')));
    var hits=new Dictionary<char,List<string>>(); var onlyTable=new Dictionary<char,bool>();
    Action<string,bool> scan=(t,tbl)=>{ foreach(char ch in t??"") if(IsBroken(ch)&&docAll.IndexOf(ch)<0){ if(!hits.ContainsKey(ch)){ hits[ch]=new List<string>(); onlyTable[ch]=true; } hits[ch].Add(t); if(!tbl) onlyTable[ch]=false; } };
    foreach(var l in p.Lines) scan(l,false);
    foreach(var t in p.Tables) foreach(var row in t) foreach(var c in row) scan(c,true);
    foreach(var kv in hits){
     string ex=Tx.Cut(kv.Value[0],30), where=onlyTable[kv.Key]?TableLabel()+" ":"", ch=kv.Key.ToString();
     string arrow="àè".IndexOf(kv.Key)>=0?"→":kv.Key=='ß'?"←":kv.Key=='á'?"↑":kv.Key=='â'?"↓":null;
     if(arrow!=null) fBroken.Add(where+"화살표 "+kv.Value.Count+"곳이 '"+Tx.Red(ch)+"'로 깨짐 (예: '"+ex+"') → '"+arrow+"'로 수정");
     else fBroken.Add(where+"깨진 문자 '"+Tx.Red(ch)+"' "+kv.Value.Count+"곳 (예: '"+ex+"') → 원고대로 수정");
    }
   }
   static string CellKey(string s){ return Tx.N2(Regex.Replace(s??"",@"[\u00C0-\u00FF\uFFFD\uF000-\uF0FF]","")); }
   static string RowKey(List<string> row){ return String.Concat(row.Select(CellKey)); }
   void Tables(){
    foreach(var dt in m.Tables){
     if(!DataTable(dt)) continue;
     string dAll=String.Concat(dt.Rows.Select(RowKey)); if(dAll.Length<6) continue;
     string label=dAll.Contains("요금")?"요금제 표":"표";
     // 블로그에 글자 표가 있을 때만 비교하고, 이미지로 넣은 표는 확인 안내만 한다
     int bi=0; double bs=-1;
     for(int i=0;i<p.Tables.Count;i++){ double s=Tx.Dice(dAll,String.Concat(p.Tables[i].Select(RowKey))); if(s>bs){ bs=s; bi=i; } }
     if(bs<0.3){ string msg=label+Tx.Josa(label,"이","가")+" 이미지로 들어감 → 확인 바람"; if(!R.Notes.Contains(msg)) R.Notes.Insert(0,msg); continue; }
     // 표는 칸 나누기·머리글이 블로그마다 달라서 숫자(금액·데이터 등)만 비교한다
     var pt=p.Tables[bi]; var diffs=new List<string>();
     var docLeft=Nums(dt.Rows.SelectMany(x=>x)).Select(x=>x.Key).ToList(); var postLeft=Nums(pt.SelectMany(x=>x)).Select(x=>x.Key).ToList();
     foreach(var k in docLeft.ToList()) if(postLeft.Remove(k)) docLeft.Remove(k);
     foreach(var row in dt.Rows){
      var miss=Nums(row).Where(n=>docLeft.Contains(n.Key)).ToList(); if(miss.Count==0) continue;
      var best=pt.OrderByDescending(x=>Tx.Dice(RowKey(row),RowKey(x))).First();
      var cand=Nums(best).Where(n=>postLeft.Contains(n.Key)).ToList(); string where=Tx.Cut(row.FirstOrDefault(c=>Tx.N2(c).Length>0)??"",20);
      foreach(var n in miss){
       docLeft.Remove(n.Key);
       var pn=cand.FirstOrDefault(c=>n.Unit.Length>0?c.Unit==n.Unit:c.Unit.Length==0&&c.Key.Length==n.Key.Length);
       if(pn!=null){ cand.Remove(pn); postLeft.Remove(pn.Key); diffs.Add("'"+Tx.Red(pn.Text)+"' → '"+n.Text+"'"+(where.Length>0?" ("+where+")":"")); }
       else diffs.Add("'"+n.Text+"' 없음"+(where.Length>0?" ("+where+")":""));
      }
     }
     int ds=dt.StrikeCells, ps=p.TableStrikes[bi];
     if(ds>0&&ps==0) fTable.Add(label+(Regex.IsMatch(dt.StrikeSample,@"\d")?" 정가":"")+" 취소선 없음"+(diffs.Count==0?", 수치는 원고와 동일":""));
     else if(ds>0&&ps<ds) fTable.Add(label+" 취소선 일부 없음 (원고 "+ds+"곳 / 블로그 "+ps+"곳)");
     foreach(var d in diffs.Take(3)) fTable.Add(label+" 수치 다름: "+d);
     if(diffs.Count>3) fTable.Add(label+" 수치 다름 외 "+(diffs.Count-3)+"곳");
    }
   }
   // 금액·데이터 같은 숫자만 (5G, Z8처럼 이름에 붙은 숫자는 뺀다)
   class Num { public string Key, Unit, Text; }
   static readonly Regex NumRx=new Regex(@"(?<![\dA-Za-z.,])\d[\d,]*(?:\.\d+)?(?:\s*(만\s*원|원|GB|MB|Mbps|Kbps|분|건|%|개월|P|만))?(?![A-Za-z가-힣\d])",RegexOptions.IgnoreCase);
   static List<Num> Nums(IEnumerable<string> cells){ var l=new List<Num>(); foreach(var c in cells) foreach(Match x in NumRx.Matches(c??"")) l.Add(new Num{Key=Regex.Replace(x.Groups[0].Value.Substring(0,x.Groups[0].Value.Length-x.Groups[1].Value.Length),@"[^\d.]",""),Unit=Regex.Replace(x.Groups[1].Value,@"\s","").ToLowerInvariant(),Text=x.Value.Trim()}); return l; }
   static bool DataTable(DTable t){ return t.Rows.Count>=2&&t.Rows.Any(r=>r.Count(c=>Tx.N2(c).Length>0)>=2)&&Nums(t.Rows.SelectMany(x=>x)).Count>=3; }
  }
 }

 // ───────────────────────── 엑셀(.xlsx) 저장: 어제 검수 파일과 같은 양식
 public class XRow { public int No; public string Name="", Url="", Text=""; }
 public static class Xlsx {
  const string NsMain="http://schemas.openxmlformats.org/spreadsheetml/2006/main", NsRel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  static string Esc(string s){ var sb=new StringBuilder(); foreach(char c in s??""){ if(c<0x20&&c!='\n'&&c!='\t') continue; switch(c){ case '&': sb.Append("&amp;"); break; case '<': sb.Append("&lt;"); break; case '>': sb.Append("&gt;"); break; case '"': sb.Append("&quot;"); break; default: sb.Append(c); break; } } return sb.ToString(); }
  static string F(double d){ return d.ToString("0.##",CultureInfo.InvariantCulture); }
  static double Units(string s){ return (s??"").Sum(c=>c>=0x1100?2.0:1.0); }
  static string Rich(string marked){
   var sb=new StringBuilder("<is>"); bool red=false; var cur=new StringBuilder(); bool any=false;
   Action flush=()=>{ if(cur.Length==0) return; any=true; sb.Append("<r><rPr><sz val=\"9\"/><color rgb=\""+(red?"FFFF0000":"FF000000")+"\"/><rFont val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/></rPr><t xml:space=\"preserve\">"+Esc(cur.ToString())+"</t></r>"); cur.Clear(); };
   foreach(char c in (marked??"").Replace("\r\n","\n")){ if(c==Tx.RO){ flush(); red=true; } else if(c==Tx.RC){ flush(); red=false; } else cur.Append(c); }
   flush(); if(!any) sb.Append("<t></t>"); return sb.Append("</is>").ToString();
  }
  static string Str(string s){ return "<is><t xml:space=\"preserve\">"+Esc(s)+"</t></is>"; }
  public static double Height(string text){ int lines=0; foreach(var l in Tx.Plain(text).Split('\n')) lines+=Math.Max(1,(int)Math.Ceiling(Units(l)/100.0)); return lines<=1?16.5:12.0*lines; }
  static void Put(ZipArchive z,string name,string xml){ var e=z.CreateEntry(name,CompressionLevel.Optimal); using(var w=new StreamWriter(e.Open(),new UTF8Encoding(false))) w.Write(xml); }
  public static void Save(string path,List<KeyValuePair<string,List<XRow>>> sheets){
   string tmp=path+".tmp"; if(File.Exists(tmp)) File.Delete(tmp);
   const string head="<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
   using(var fs=new FileStream(tmp,FileMode.CreateNew)) using(var z=new ZipArchive(fs,ZipArchiveMode.Create)){
    var ct=new StringBuilder(head+"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
    var wb=new StringBuilder(head+"<workbook xmlns=\""+NsMain+"\" xmlns:r=\""+NsRel+"\"><bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
    var wr=new StringBuilder(head+"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
    for(int i=0;i<sheets.Count;i++){
     int n=i+1; ct.Append("<Override PartName=\"/xl/worksheets/sheet"+n+".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
     wb.Append("<sheet name=\""+Esc(SafeName(sheets[i].Key))+"\" sheetId=\""+n+"\" r:id=\"rId"+n+"\"/>");
     wr.Append("<Relationship Id=\"rId"+n+"\" Type=\""+NsRel+"/worksheet\" Target=\"worksheets/sheet"+n+".xml\"/>");
     List<string> links; Put(z,"xl/worksheets/sheet"+n+".xml",Sheet(sheets[i].Value,i==0,out links));
     if(links.Count>0){ var sr=new StringBuilder(head+"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"); for(int k=0;k<links.Count;k++) sr.Append("<Relationship Id=\"rId"+(k+1)+"\" Type=\""+NsRel+"/hyperlink\" Target=\""+Esc(links[k])+"\" TargetMode=\"External\"/>"); Put(z,"xl/worksheets/_rels/sheet"+n+".xml.rels",sr.Append("</Relationships>").ToString()); }
    }
    wr.Append("<Relationship Id=\"rId"+(sheets.Count+1)+"\" Type=\""+NsRel+"/styles\" Target=\"styles.xml\"/></Relationships>");
    Put(z,"[Content_Types].xml",ct.Append("</Types>").ToString());
    Put(z,"_rels/.rels",head+"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\""+NsRel+"/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
    Put(z,"xl/workbook.xml",wb.Append("</sheets></workbook>").ToString());
    Put(z,"xl/_rels/workbook.xml.rels",wr.ToString());
    string font="<name val=\"맑은 고딕\"/><family val=\"3\"/><charset val=\"129\"/>", border="<border><left style=\"thin\"><color auto=\"1\"/></left><right style=\"thin\"><color auto=\"1\"/></right><top style=\"thin\"><color auto=\"1\"/></top><bottom style=\"thin\"><color auto=\"1\"/></bottom><diagonal/></border>";
    string al="<alignment horizontal=\"left\" vertical=\"center\"/>";
    Put(z,"xl/styles.xml",head+"<styleSheet xmlns=\""+NsMain+"\"><fonts count=\"4\"><font><sz val=\"11\"/>"+font+"</font><font><b/><sz val=\"9\"/><color rgb=\"FFFFFFFF\"/>"+font+"</font><font><sz val=\"9\"/><color rgb=\"FF000000\"/>"+font+"</font><font><u/><sz val=\"9\"/><color rgb=\"FF467886\"/>"+font+"</font></fonts>"
     +"<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF000000\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF5050\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>"
     +"<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>"+border+"</borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"6\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
     +"<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\">"+al+"</xf>"
     +"<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\">"+al+"</xf>"
     +"<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\">"+al+"</xf>"
     +"<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\">"+al+"</xf>"
     +"<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\" wrapText=\"1\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"표준\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
   }
   if(File.Exists(path)) File.Delete(path); File.Move(tmp,path);
  }
  public static string SafeName(string s){ s=Regex.Replace(s??"",@"[\[\]:*?/\\]","-"); return s.Length>31?s.Substring(0,31):(s.Length==0?"Sheet1":s); }
  static string Sheet(List<XRow> rows,bool first,out List<string> links){
   links=new List<string>(); int last=2+rows.Count;
   double wC=Math.Max(5.38,rows.Select(r=>Units(r.Name)).DefaultIfEmpty(0).Max()*0.85+0.6), wD=Math.Max(30,rows.Select(r=>(double)(r.Url??"").Length).DefaultIfEmpty(0).Max()*0.84+0.5);
   var sb=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\""+NsMain+"\" xmlns:r=\""+NsRel+"\"><dimension ref=\"B2:E"+last+"\"/><sheetViews><sheetView "+(first?"tabSelected=\"1\" ":"")+"workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"16.5\"/>");
   sb.Append("<cols><col min=\"2\" max=\"2\" width=\"3.88\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\""+F(wC)+"\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\""+F(wD)+"\" customWidth=\"1\"/><col min=\"5\" max=\"5\" width=\"86\" customWidth=\"1\"/></cols><sheetData>");
   sb.Append("<row r=\"2\" spans=\"2:5\" ht=\"16.5\" customHeight=\"1\">");
   string[] hd={"순서","이름","URL","수정사항"}; for(int i=0;i<4;i++) sb.Append("<c r=\""+(char)('B'+i)+"2\" s=\"1\" t=\"inlineStr\">"+Str(hd[i])+"</c>");
   sb.Append("</row>");
   for(int i=0;i<rows.Count;i++){
    var x=rows[i]; int r=3+i; string plain=Tx.Clean(Tx.Plain(x.Text)); bool bad=plain.Length>0&&plain!="이상 없음";
    sb.Append("<row r=\""+r+"\" spans=\"2:5\" ht=\""+F(Height(x.Text))+"\" customHeight=\"1\"><c r=\"B"+r+"\" s=\"2\"><v>"+x.No+"</v></c>");
    sb.Append("<c r=\"C"+r+"\" s=\""+(bad?3:2)+"\" t=\"inlineStr\">"+Str(x.Name)+"</c><c r=\"D"+r+"\" s=\"4\" t=\"inlineStr\">"+Str(x.Url)+"</c>");
    sb.Append("<c r=\"E"+r+"\" s=\"5\" t=\"inlineStr\">"+Rich(x.Text)+"</c></row>");
    if(!String.IsNullOrEmpty(x.Url)) links.Add(x.Url);
   }
   sb.Append("</sheetData>");
   if(links.Count>0){ sb.Append("<hyperlinks>"); int k=0; for(int i=0;i<rows.Count;i++) if(!String.IsNullOrEmpty(rows[i].Url)) sb.Append("<hyperlink ref=\"D"+(3+i)+"\" r:id=\"rId"+(++k)+"\"/>"); sb.Append("</hyperlinks>"); }
   return sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>").ToString();
  }
  public static DateTime PostDate(string date){
   var m=Regex.Match(date??"",@"(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})");
   if(m.Success) return new DateTime(Int32.Parse(m.Groups[1].Value),Int32.Parse(m.Groups[2].Value),Int32.Parse(m.Groups[3].Value));
   var d=DateTime.Now; var h=Regex.Match(date??"",@"(\d+)\s*시간"); if(h.Success) d=d.AddHours(-Int32.Parse(h.Groups[1].Value));
   var mi=Regex.Match(date??"",@"(\d+)\s*분"); if(mi.Success) d=d.AddMinutes(-Int32.Parse(mi.Groups[1].Value));
   return d.Date;
  }
  public static string SheetName(DateTime d){ return (d.Year%100).ToString("00")+"."+d.Month+"."+d.Day; }
 }

 // ───────────────────────── 아이콘: 검은 바탕에 주황·흰 서류가 겹친 모양
 public static class Logo {
  public static GraphicsPath Round(RectangleF r,float rad){ var p=new GraphicsPath(); float d=rad*2; p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p; }
  public static void Draw(Graphics g,float s){
   g.SmoothingMode=SmoothingMode.AntiAlias; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
   using(var bg=Round(new RectangleF(0,0,s,s),s*0.22f)) using(var b=new SolidBrush(Color.FromArgb(30,30,32))) g.FillPath(b,bg);
   Doc(g,s*0.15f,s*0.14f,s*0.44f,s*0.54f,-10f,Color.FromArgb(255,104,26),Color.FromArgb(150,255,236,224),s,false);
   Doc(g,s*0.41f,s*0.32f,s*0.44f,s*0.54f,6f,Color.White,Color.FromArgb(205,205,214),s,true);
  }
  static void Doc(Graphics g,float x,float y,float w,float h,float angle,Color fill,Color line,float s,bool front){
   var st=g.Save(); g.TranslateTransform(x+w/2,y+h/2); g.RotateTransform(angle);
   var r=new RectangleF(-w/2,-h/2,w,h); float fold=w*0.3f;
   using(var path=new GraphicsPath()){
    path.AddLine(r.Left,r.Top,r.Right-fold,r.Top); path.AddLine(r.Right-fold,r.Top,r.Right,r.Top+fold); path.AddLine(r.Right,r.Top+fold,r.Right,r.Bottom); path.AddLine(r.Right,r.Bottom,r.Left,r.Bottom); path.CloseFigure();
    if(front){ g.TranslateTransform(-s*0.02f,s*0.025f); using(var sh=new SolidBrush(Color.FromArgb(110,0,0,0))) g.FillPath(sh,path); g.TranslateTransform(s*0.02f,-s*0.025f); }
    using(var b=new SolidBrush(fill)) g.FillPath(b,path);
   }
   using(var corner=new GraphicsPath()){ corner.AddLine(r.Right-fold,r.Top,r.Right-fold,r.Top+fold); corner.AddLine(r.Right-fold,r.Top+fold,r.Right,r.Top+fold); corner.CloseFigure(); using(var b=new SolidBrush(front?Color.FromArgb(214,214,222):Color.FromArgb(205,80,14))) g.FillPath(b,corner); }
   if(s>=24){ using(var pen=new Pen(line,Math.Max(1f,s*0.035f)){StartCap=LineCap.Round,EndCap=LineCap.Round}){ float lx=r.Left+w*0.18f; for(int i=0;i<3;i++){ float ly=r.Top+h*(0.42f+i*0.17f); g.DrawLine(pen,lx,ly,r.Right-w*(i==2?0.42f:0.18f),ly); } } }
   g.Restore(st);
  }
  static void Dib(Bitmap bmp,Stream s){
   int w=bmp.Width,h=bmp.Height; var bw=new BinaryWriter(s);
   bw.Write(40); bw.Write(w); bw.Write(h*2); bw.Write((short)1); bw.Write((short)32); for(int i=0;i<6;i++) bw.Write(0);
   for(int y=h-1;y>=0;y--) for(int x=0;x<w;x++){ var c=bmp.GetPixel(x,y); bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A); }
   int stride=((w+31)/32)*4;
   for(int y=h-1;y>=0;y--){ var row=new byte[stride]; for(int x=0;x<w;x++) if(bmp.GetPixel(x,y).A<128) row[x/8]|=(byte)(0x80>>(x%8)); bw.Write(row); }
   bw.Flush();
  }
  public static Bitmap Bitmap(int z){ var bmp=new Bitmap(z,z,PixelFormat.Format32bppArgb); using(var g=Graphics.FromImage(bmp)){ g.Clear(Color.Transparent); Draw(g,z); } return bmp; }
  public static void SaveIco(string path){
   int[] sizes={16,24,32,48,64,128,256}; var pngs=new List<byte[]>();
   // 256은 PNG, 나머지는 예전 방식(BMP)으로 넣어야 어디서든 아이콘이 보인다
   foreach(int z in sizes) using(var bmp=Bitmap(z)) using(var ms=new MemoryStream()){ if(z>=256) bmp.Save(ms,ImageFormat.Png); else Dib(bmp,ms); pngs.Add(ms.ToArray()); }
   using(var fs=File.Create(path)) using(var w=new BinaryWriter(fs)){
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int off=6+16*sizes.Length;
    for(int i=0;i<sizes.Length;i++){ byte d=(byte)(sizes[i]>=256?0:sizes[i]); w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(off); off+=pngs[i].Length; }
    foreach(var p in pngs) w.Write(p);
   }
  }
 }

 // ───────────────────────── 화면 부품 (블로거 서쳐와 같은 모양)
 public class CardPanel:Panel {
  public CardPanel(){ DoubleBuffered=true; BackColor=Color.White; Padding=new Padding(18); }
  protected override void OnPaintBackground(PaintEventArgs e){
   e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var path=Logo.Round(new RectangleF(0,0,Width-1,Height-1),14)) using(var brush=new SolidBrush(BackColor)) using(var pen=new Pen(Color.FromArgb(226,226,235))){ e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path); }
  }
 }
 public class SoftButton:Button {
  public SoftButton(){ FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; Height=34; BackColor=Color.FromArgb(239,239,244); ForeColor=Color.FromArgb(35,35,38); Font=new Font("맑은 고딕",9,FontStyle.Bold); }
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var p=Logo.Round(new RectangleF(0,0,Width-1,Height-1),Math.Min(9,Math.Min(Width,Height)/2f))) using(var b=new SolidBrush(BackColor)) e.Graphics.FillPath(b,p);
   TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Color.FromArgb(155,155,163),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  }
 }
 public class Pill:Control {
  public Pill(){ SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); Font=new Font("맑은 고딕",8.5f,FontStyle.Bold); Size=new Size(70,26); }
  protected override void OnTextChanged(EventArgs e){ base.OnTextChanged(e); Invalidate(); }
  protected override void OnPaint(PaintEventArgs e){
   e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var p=Logo.Round(new RectangleF(0,0,Width-1,Height-1),Math.Min(9,(Height-1)/2f))) using(var b=new SolidBrush(BackColor)) e.Graphics.FillPath(b,p);
   TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  }
 }
 public class BrandMark:Control {
  public BrandMark(){ Size=new Size(30,30); SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
  protected override void OnPaint(PaintEventArgs e){ e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor); Logo.Draw(e.Graphics,Math.Min(Width,Height)); }
 }
 public class NoteItem { public string Raw; public override string ToString(){ return Tx.Plain(Raw); } }
 public class Item { public string DocPath="", AutoName="", CheckedKey=""; public Post Post; public Result Res; public string Final; public string State="대기", Error=""; }

 public class RowView:TableLayoutPanel {
  public static readonly float[] Cols={36,112,88,190,0,88,82,36};
  public Item It; public Label NoLbl=new Label(), FileLbl=new Label(); public TextBox NameBox=new TextBox(), LinkBox=new TextBox();
  public SoftButton FileBtn=new SoftButton(), CheckBtn=new SoftButton(), DelBtn=new SoftButton(); public Pill Badge=new Pill(); bool selected;
  public RowView(Item it){
   It=it; ColumnCount=8; RowCount=1; Height=48; Margin=Padding.Empty; Padding=new Padding(0,0,8,0); BackColor=Color.White; DoubleBuffered=true;
   foreach(float w in Cols) ColumnStyles.Add(w==0?new ColumnStyle(SizeType.Percent,100):new ColumnStyle(SizeType.Absolute,w)); RowStyles.Add(new RowStyle(SizeType.Percent,100));
   NoLbl.Dock=DockStyle.Fill; NoLbl.TextAlign=ContentAlignment.MiddleCenter; NoLbl.ForeColor=MainForm.Muted;
   foreach(var tb in new[]{NameBox,LinkBox}){ tb.Anchor=AnchorStyles.Left|AnchorStyles.Right; tb.BorderStyle=BorderStyle.FixedSingle; tb.BackColor=MainForm.Surface; tb.Margin=new Padding(0,0,8,0); }
   FileBtn.Text="파일 넣기"; FileBtn.Anchor=AnchorStyles.Left|AnchorStyles.Right; FileBtn.Height=30; FileBtn.Margin=new Padding(0,0,8,0);
   FileLbl.Dock=DockStyle.Fill; FileLbl.TextAlign=ContentAlignment.MiddleLeft; FileLbl.AutoEllipsis=true; FileLbl.Margin=new Padding(0,0,8,0);
   CheckBtn.Text="검수하기"; CheckBtn.BackColor=MainForm.Ink; CheckBtn.ForeColor=Color.White; CheckBtn.Anchor=AnchorStyles.Left|AnchorStyles.Right; CheckBtn.Height=30; CheckBtn.Margin=new Padding(0,0,8,0);
   Badge.Anchor=AnchorStyles.None; Badge.Size=new Size(72,26);
   DelBtn.Text="×"; DelBtn.BackColor=Color.White; DelBtn.ForeColor=MainForm.Muted; DelBtn.Size=new Size(28,28); DelBtn.Anchor=AnchorStyles.None; DelBtn.Font=new Font("맑은 고딕",11,FontStyle.Bold);
   Controls.Add(NoLbl,0,0); Controls.Add(NameBox,1,0); Controls.Add(FileBtn,2,0); Controls.Add(FileLbl,3,0); Controls.Add(LinkBox,4,0); Controls.Add(CheckBtn,5,0); Controls.Add(Badge,6,0); Controls.Add(DelBtn,7,0);
   RefreshView();
  }
  public bool Selected { get { return selected; } set { selected=value; BackColor=value?MainForm.SelC:Color.White; DelBtn.BackColor=BackColor; foreach(Control c in Controls) c.Invalidate(); Invalidate(); } }
  protected override void OnPaint(PaintEventArgs e){ base.OnPaint(e); using(var pen=new Pen(MainForm.LineC)) e.Graphics.DrawLine(pen,0,Height-1,Width,Height-1); }
  public void RefreshView(){
   FileLbl.Text=It.DocPath==""?"원고를 끌어다 놓아도 돼요":System.IO.Path.GetFileName(It.DocPath); FileLbl.ForeColor=It.DocPath==""?Color.FromArgb(165,165,172):MainForm.Ink;
   string s=It.State; Badge.Text=s;
   if(s=="일치"){ Badge.BackColor=Color.FromArgb(225,245,233); Badge.ForeColor=Color.FromArgb(37,133,69); }
   else if(s=="불일치"){ Badge.BackColor=Color.FromArgb(255,231,228); Badge.ForeColor=Color.FromArgb(214,48,49); }
   else if(s=="오류"){ Badge.BackColor=Color.FromArgb(255,240,222); Badge.ForeColor=Color.FromArgb(196,98,0); }
   else if(s=="검수 중"){ Badge.BackColor=MainForm.Ink; Badge.ForeColor=Color.White; }
   else { Badge.BackColor=Color.FromArgb(239,239,244); Badge.ForeColor=MainForm.Muted; }
  }
 }

 public class MainForm:Form {
  public static readonly Color Orange=Color.FromArgb(255,104,26), Ink=Color.FromArgb(30,30,32), Muted=Color.FromArgb(126,126,135), Surface=Color.FromArgb(242,242,248), LineC=Color.FromArgb(235,235,243), SelC=Color.FromArgb(255,244,236), RedText=Color.FromArgb(230,0,0);
  FlowLayoutPanel rowsPanel=new FlowLayoutPanel(); TableLayoutPanel colHead; RowView sel; RichTextBox rtb=new RichTextBox(); ListBox notes=new ListBox();
  Label detailInfo=new Label(), status=new Label(), detailTitle=new Label(); Pill chipAll=new Pill(), chipOk=new Pill(), chipBad=new Pill();
  TextBox logs=new TextBox(); Panel logPanel=new Panel(); SoftButton runAll=new SoftButton(), export=new SoftButton(), openPost=new SoftButton();
  bool busy, loading;
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);

  static Label Caption(string text,float size=9,bool bold=false){ return new Label{Text=text,AutoSize=true,ForeColor=Ink,Font=new Font("맑은 고딕",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,6,0,4)}; }
  static SoftButton Small(string text,Action action){ var b=new SoftButton{Text=text,AutoSize=true,Padding=new Padding(9,0,9,0),Margin=new Padding(0,2,8,2)}; b.Click+=(s,e)=>action(); return b; }
  static SoftButton TextBtn(string text,Action action){ var b=Small(text,action); b.ForeColor=Orange; b.BackColor=Color.White; b.Font=new Font("맑은 고딕",8.5f,FontStyle.Bold); b.Padding=Padding.Empty; b.Margin=new Padding(0,0,6,0); b.Height=28; return b; }

  public MainForm(){
   Text="원고 검수기"; try{ Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); }catch{}
   Size=new Size(1340,860); MinimumSize=new Size(1150,760); StartPosition=FormStartPosition.CenterScreen;
   Font=new Font("맑은 고딕",9); BackColor=Surface; ForeColor=Ink; AutoScaleMode=AutoScaleMode.Dpi; AllowDrop=true; KeyPreview=true;
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
   shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(shell);
   // 왼쪽 메뉴
   var sidebar=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=Padding.Empty}; shell.Controls.Add(sidebar,0,0);
   sidebar.Controls.Add(new BrandMark{Location=new Point(20,24)});
   sidebar.Controls.Add(new Label{Text="원고 검수기",Location=new Point(56,25),AutoSize=true,Font=new Font("맑은 고딕",11,FontStyle.Bold)});
   sidebar.Controls.Add(new Label{Text="버전 1.0",Location=new Point(57,49),AutoSize=true,ForeColor=Muted,Font=new Font("맑은 고딕",8)});
   sidebar.Controls.Add(new Label{Text="블로그",Location=new Point(22,101),AutoSize=true,ForeColor=Muted});
   var nav=Small("원고 검수",()=>{ var r=Rows().FirstOrDefault(); if(r!=null) r.LinkBox.Focus(); }); nav.Location=new Point(12,128); nav.Size=new Size(156,40); nav.AutoSize=false; nav.BackColor=Ink; nav.ForeColor=Color.White; sidebar.Controls.Add(nav);
   var nav2=Small("엑셀 내보내기",()=>Export()); nav2.Location=new Point(12,175); nav2.Size=new Size(156,38); nav2.AutoSize=false; nav2.BackColor=Color.White; sidebar.Controls.Add(nav2);
   var companion=new Panel{Height=96,Dock=DockStyle.Bottom,Padding=new Padding(18),BackColor=Surface};
   companion.Controls.Add(new Label{Text="원고 ↔ 블로그 글 대조\n검수 양식으로 엑셀 저장",Dock=DockStyle.Fill,ForeColor=Muted}); sidebar.Controls.Add(companion);
   // 오른쪽 본문
   var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24,18,24,6),Margin=Padding.Empty};
   main.RowStyles.Add(new RowStyle(SizeType.Absolute,74)); main.RowStyles.Add(new RowStyle(SizeType.Percent,100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,0)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); shell.Controls.Add(main,1,0);
   var header=new Panel{Dock=DockStyle.Fill};
   header.Controls.Add(new Label{Text="원고 검수",Location=new Point(0,1),AutoSize=true,Font=new Font("맑은 고딕",19,FontStyle.Bold)});
   header.Controls.Add(new Label{Text="원고(.docx)와 블로그 링크를 넣고 검수하기를 누르세요. 원고 여러 개를 창에 끌어다 놓아도 됩니다.",Location=new Point(1,39),AutoSize=true,ForeColor=Muted});
   var help=Small("사용 방법",()=>Help()); help.Anchor=AnchorStyles.Top|AnchorStyles.Right; header.Controls.Add(help); header.Resize+=(s,e)=>help.Location=new Point(header.ClientSize.Width-help.Width,5); main.Controls.Add(header,0,0);
   var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
   body.RowStyles.Add(new RowStyle(SizeType.Percent,56)); body.RowStyles.Add(new RowStyle(SizeType.Percent,44)); main.Controls.Add(body,0,1);
   // 검수 목록 카드
   var listCard=new CardPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,8),Padding=new Padding(18,14,18,14)}; body.Controls.Add(listCard,0,0);
   var lc=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
   lc.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); lc.RowStyles.Add(new RowStyle(SizeType.Absolute,28)); lc.RowStyles.Add(new RowStyle(SizeType.Percent,100)); lc.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); listCard.Controls.Add(lc);
   var lh=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty}; lh.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); lh.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,360));
   var chips=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty}; chips.Controls.Add(Caption("검수 목록",10,true));
   foreach(var c in new[]{chipAll,chipOk,chipBad}){ c.Margin=new Padding(8,2,0,0); c.Size=new Size(66,26); chips.Controls.Add(c); }
   chipAll.BackColor=Ink; chipAll.ForeColor=Color.White; chipOk.BackColor=Color.FromArgb(225,245,233); chipOk.ForeColor=Color.FromArgb(37,133,69); chipBad.BackColor=Color.FromArgb(255,231,228); chipBad.ForeColor=Color.FromArgb(214,48,49);
   lh.Controls.Add(chips,0,0);
   var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0,6,0,0)};
   tools.Controls.Add(TextBtn("모두 비우기",()=>ClearAll())); tools.Controls.Add(TextBtn("링크 여러 개 붙여넣기",()=>PasteLinks(null))); tools.Controls.Add(TextBtn("원고 여러 개 넣기",()=>PickFiles(null)));
   lh.Controls.Add(tools,1,0); lc.Controls.Add(lh,0,0);
   colHead=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=8,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,0,8,0)};
   string[] heads={"No","이름","원고 파일","","블로그 링크","","결과",""};
   for(int i=0;i<8;i++){ colHead.ColumnStyles.Add(RowView.Cols[i]==0?new ColumnStyle(SizeType.Percent,100):new ColumnStyle(SizeType.Absolute,RowView.Cols[i])); colHead.Controls.Add(new Label{Text=heads[i],Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font("맑은 고딕",8,FontStyle.Bold),TextAlign=i==0||i==6?ContentAlignment.MiddleCenter:ContentAlignment.MiddleLeft},i,0); }
   lc.Controls.Add(colHead,0,1);
   rowsPanel.Dock=DockStyle.Fill; rowsPanel.FlowDirection=FlowDirection.TopDown; rowsPanel.WrapContents=false; rowsPanel.AutoScroll=true; rowsPanel.BackColor=Color.White; rowsPanel.Margin=Padding.Empty;
   rowsPanel.Resize+=(s,e)=>FitRows(); lc.Controls.Add(rowsPanel,0,2);
   var lb=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty}; lb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); lb.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   var add=Small("+ 추가하기",()=>{ var r=AddRow(new Item()); Select(r); r.NameBox.Focus(); }); add.BackColor=Color.FromArgb(255,241,229); add.ForeColor=Orange; add.AutoSize=false; add.Size=new Size(120,38); add.Anchor=AnchorStyles.Left; add.Margin=new Padding(0,10,0,0); lb.Controls.Add(add,0,0);
   var big=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,6,0,0),Anchor=AnchorStyles.Right};
   runAll.Text="▶  전체 검수"; runAll.BackColor=Ink; runAll.ForeColor=Color.White; runAll.Size=new Size(160,44); runAll.Font=new Font("맑은 고딕",11,FontStyle.Bold); runAll.Margin=new Padding(0,0,10,0); runAll.Click+=async(s,e)=>await RunAll(); big.Controls.Add(runAll);
   export.Text="엑셀 내보내기"; export.BackColor=Orange; export.ForeColor=Color.White; export.Size=new Size(170,44); export.Font=new Font("맑은 고딕",11,FontStyle.Bold); export.Margin=Padding.Empty; export.Click+=(s,e)=>Export(); big.Controls.Add(export);
   lb.Controls.Add(big,1,0); lc.Controls.Add(lb,0,3);
   // 수정사항 카드
   var detailCard=new CardPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(18,12,18,16)}; body.Controls.Add(detailCard,0,1);
   var dc=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty}; dc.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); dc.RowStyles.Add(new RowStyle(SizeType.Percent,100)); detailCard.Controls.Add(dc);
   var dh=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty}; dh.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); dh.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,390));
   var dl=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty}; detailTitle=Caption("수정사항",10,true); dl.Controls.Add(detailTitle);
   detailInfo.AutoSize=true; detailInfo.ForeColor=Muted; detailInfo.Margin=new Padding(10,8,0,0); dl.Controls.Add(detailInfo); dh.Controls.Add(dl,0,0);
   var dt=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=new Padding(0,2,0,0)};
   openPost.Text="블로그 열기"; openPost.AutoSize=true; openPost.Padding=new Padding(9,0,9,0); openPost.Margin=new Padding(0,2,0,2); openPost.Click+=(s,e)=>{ if(sel!=null&&sel.It.Post!=null) Open(sel.It.Post.Url); }; dt.Controls.Add(openPost);
   dt.Controls.Add(Small("이상 없음",()=>{ if(sel==null||rtb.ReadOnly) return; WriteRich("이상 없음"); Commit(); }));
   dt.Controls.Add(Small("검정 글씨",()=>PaintSel(false))); var redBtn=Small("빨간 글씨",()=>PaintSel(true)); redBtn.ForeColor=RedText; dt.Controls.Add(redBtn);
   dh.Controls.Add(dt,1,0); dc.Controls.Add(dh,0,0);
   var split=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty}; split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38)); dc.Controls.Add(split,0,1);
   var rtbWrap=new Panel{Dock=DockStyle.Fill,BackColor=Surface,Padding=new Padding(12,10,8,8),Margin=new Padding(0,0,10,0)};
   rtb.Dock=DockStyle.Fill; rtb.BorderStyle=BorderStyle.None; rtb.BackColor=Surface; rtb.Font=new Font("맑은 고딕",10); rtb.DetectUrls=false; rtb.ScrollBars=RichTextBoxScrollBars.Vertical;
   rtb.Leave+=(s,e)=>Commit(); rtbWrap.Controls.Add(rtb); split.Controls.Add(rtbWrap,0,0);
   var nt=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty}; nt.RowStyles.Add(new RowStyle(SizeType.Absolute,24)); nt.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   nt.Controls.Add(new Label{Text="참고 · 더블클릭하면 수정사항에 추가",Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font("맑은 고딕",8.5f,FontStyle.Bold)},0,0);
   var nWrap=new Panel{Dock=DockStyle.Fill,BackColor=Surface,Padding=new Padding(8,8,4,4),Margin=Padding.Empty};
   notes.Dock=DockStyle.Fill; notes.BorderStyle=BorderStyle.None; notes.BackColor=Surface; notes.IntegralHeight=false; notes.HorizontalScrollbar=true; notes.Font=new Font("맑은 고딕",9);
   notes.DoubleClick+=(s,e)=>{ var n=notes.SelectedItem as NoteItem; if(n!=null) AddToFinal(Regex.Replace(n.Raw,"^오타 의심: ","오타: ")); };
   nWrap.Controls.Add(notes); nt.Controls.Add(nWrap,0,1); split.Controls.Add(nt,1,0);
   // 실행 로그와 상태줄
   logPanel.Dock=DockStyle.Fill; logPanel.Visible=false; logPanel.Margin=new Padding(0,8,0,0); logs.Dock=DockStyle.Fill; logs.Multiline=true; logs.ReadOnly=true; logs.ScrollBars=ScrollBars.Vertical; logs.BackColor=Color.White; logs.BorderStyle=BorderStyle.FixedSingle; logPanel.Controls.Add(logs); main.Controls.Add(logPanel,0,2);
   var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty}; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,116));
   status.Dock=DockStyle.Fill; status.ForeColor=Muted; status.Font=new Font("맑은 고딕",8); status.TextAlign=ContentAlignment.MiddleLeft; footer.Controls.Add(status,0,0);
   var toggle=new LinkLabel{Text="실행 로그 보기",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,LinkColor=Muted,ActiveLinkColor=Orange,VisitedLinkColor=Muted};
   toggle.LinkClicked+=(s,e)=>{ logPanel.Visible=!logPanel.Visible; main.RowStyles[2].Height=logPanel.Visible?115:0; toggle.Text=logPanel.Visible?"실행 로그 닫기":"실행 로그 보기"; }; footer.Controls.Add(toggle,1,0); main.Controls.Add(footer,0,3);
   DragEnter+=OnDragEnter; DragDrop+=(s,e)=>OnDrop(e,null);
   FormClosing+=(s,e)=>{ if(busy&&MessageBox.Show("검수 중이에요. 그래도 닫을까요?","원고 검수기",MessageBoxButtons.YesNo)!=DialogResult.Yes) e.Cancel=true; };
   for(int i=0;i<3;i++) AddRow(new Item());
   Select(Rows()[0]); UpdateChips();
  }

  // ── 행 관리
  List<RowView> Rows(){ return rowsPanel.Controls.Cast<RowView>().ToList(); }
  void FitRows(){
   int w=rowsPanel.ClientSize.Width; foreach(Control c in rowsPanel.Controls) if(c.Width!=w) c.Width=w;
   colHead.Padding=new Padding(0,0,8+(rowsPanel.VerticalScroll.Visible?SystemInformation.VerticalScrollBarWidth:0),0);
  }
  RowView AddRow(Item it){
   var r=new RowView(it); r.Width=Math.Max(400,rowsPanel.ClientSize.Width);
   Action pick=()=>Select(r);
   foreach(Control c in r.Controls){ c.MouseDown+=(s,e)=>pick(); c.Enter+=(s,e)=>pick(); c.AllowDrop=true; c.DragEnter+=OnDragEnter; c.DragDrop+=(s,e)=>OnDrop(e,r); }
   r.MouseDown+=(s,e)=>pick(); r.AllowDrop=true; r.DragEnter+=OnDragEnter; r.DragDrop+=(s,e)=>OnDrop(e,r);
   r.FileBtn.Click+=(s,e)=>PickFiles(r);
   r.CheckBtn.Click+=async(s,e)=>{ if(busy) return; busy=true; SetBusy(true); try{ await CheckRow(r); } finally{ busy=false; SetBusy(false); } };
   r.DelBtn.Click+=(s,e)=>RemoveRow(r);
   r.NameBox.TextChanged+=(s,e)=>{ if(sel==r) UpdateDetailTitle(); };
   r.LinkBox.TextChanged+=(s,e)=>{ if(r.It.CheckedKey!=""&&r.It.CheckedKey!=r.It.DocPath+"|"+Tx.Clean(r.LinkBox.Text)) Stale(r); };
   r.LinkBox.KeyDown+=(s,e)=>{
    if(e.Control&&e.KeyCode==Keys.V){ string t=""; try{ t=Clipboard.GetText(); }catch{} var urls=ExtractUrls(t); if(urls.Count>1){ e.SuppressKeyPress=true; AddLinks(urls,r); } }
    else if(e.KeyCode==Keys.Enter){ e.SuppressKeyPress=true; r.CheckBtn.PerformClick(); }
   };
   rowsPanel.Controls.Add(r); Renumber(); FitRows(); UpdateChips(); return r;
  }
  void RemoveRow(RowView r){
   if(busy) return;
   if(sel==r){ Select(null); }
   rowsPanel.Controls.Remove(r); r.Dispose();
   if(rowsPanel.Controls.Count==0) AddRow(new Item());
   Renumber(); FitRows(); UpdateChips(); if(sel==null) Select(Rows()[0]);
  }
  void ClearAll(){
   if(busy) return;
   if(Rows().Any(r=>r.It.DocPath!=""||r.LinkBox.Text.Trim()!="")&&MessageBox.Show("목록을 모두 비울까요?","원고 검수기",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
   Select(null); foreach(var r in Rows()){ rowsPanel.Controls.Remove(r); r.Dispose(); }
   for(int i=0;i<3;i++) AddRow(new Item()); Select(Rows()[0]);
  }
  void Renumber(){ int i=1; foreach(var r in Rows()) r.NoLbl.Text=(i++).ToString(); }
  void Stale(RowView r){ var it=r.It; if(it.State=="대기") return; it.State="대기"; it.Res=null; it.Post=null; it.Final=null; it.CheckedKey=""; it.Error=""; r.RefreshView(); if(sel==r) LoadDetail(); UpdateChips(); }
  void Select(RowView r){
   if(sel==r) return; Commit();
   if(sel!=null&&!sel.IsDisposed) sel.Selected=false;
   sel=r; if(r!=null) r.Selected=true; LoadDetail();
  }
  void SetBusy(bool b){ runAll.Enabled=!b; export.Enabled=!b; foreach(var r in Rows()){ r.CheckBtn.Enabled=!b; r.DelBtn.Enabled=!b; } UseWaitCursor=false; }

  // ── 파일·링크 넣기
  void OnDragEnter(object s,DragEventArgs e){ if(e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.Text)) e.Effect=DragDropEffects.Copy; }
  void OnDrop(DragEventArgs e,RowView target){
   if(e.Data.GetDataPresent(DataFormats.FileDrop)) AddFiles((string[])e.Data.GetData(DataFormats.FileDrop),target);
   else if(e.Data.GetDataPresent(DataFormats.Text)){ var urls=ExtractUrls(Convert.ToString(e.Data.GetData(DataFormats.Text))); if(urls.Count>0) AddLinks(urls,target); }
  }
  void PickFiles(RowView target){
   using(var d=new OpenFileDialog{Filter="워드 원고 (*.docx)|*.docx",Multiselect=true,Title="원고 파일 선택"}) if(d.ShowDialog(this)==DialogResult.OK) AddFiles(d.FileNames,target);
  }
  void AddFiles(IEnumerable<string> files,RowView target){
   var all=files.ToList(); var docs=all.Where(f=>f.EndsWith(".docx",StringComparison.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(f).StartsWith("~$")).ToList();
   if(docs.Count==0){ MessageBox.Show(all.Any(f=>f.EndsWith(".doc",StringComparison.OrdinalIgnoreCase))?".doc 파일은 워드에서 .docx로 다시 저장한 뒤 넣어 주세요.":"워드 원고(.docx)만 넣을 수 있어요.","원고 검수기"); return; }
   int k=0; if(target!=null){ SetDoc(target,docs[0]); k=1; }
   var rows=Rows(); int from=target==null?0:rows.IndexOf(target);
   for(;k<docs.Count;k++){ var r=rows.Skip(Math.Max(0,from)).FirstOrDefault(x=>x.It.DocPath=="")??AddRow(new Item()); SetDoc(r,docs[k]); rows=Rows(); }
   Log("원고 "+docs.Count+"개 추가");
  }
  void SetDoc(RowView r,string path){
   string nm=Manuscript.NameFromFile(path);
   if(r.NameBox.Text.Trim()==""||r.NameBox.Text==r.It.AutoName) r.NameBox.Text=nm;
   r.It.AutoName=nm; r.It.DocPath=path; Stale(r); r.RefreshView(); if(sel==r) UpdateDetailTitle();
  }
  static List<string> ExtractUrls(string text){ return Regex.Matches(text??"",@"(?:https?://)?(?:m\.)?(?:blog\.naver\.com|naver\.me)/[^\s<>""']+",RegexOptions.IgnoreCase).Cast<Match>().Select(m=>Checker.TrimUrl(m.Value)).ToList(); }
  void PasteLinks(RowView start){
   string t=""; try{ t=Clipboard.GetText(); }catch{}
   var urls=ExtractUrls(t); if(urls.Count==0){ MessageBox.Show("클립보드에 네이버 블로그 링크가 없어요. 링크를 복사한 뒤 다시 눌러 주세요.","원고 검수기"); return; }
   AddLinks(urls,start);
  }
  void AddLinks(List<string> urls,RowView start){
   var rows=Rows(); int i=start==null?0:Math.Max(0,rows.IndexOf(start)); bool first=start!=null;
   foreach(var u in urls){
    RowView r=null;
    while(i<rows.Count){ var x=rows[i++]; if((first&&x==start)||x.LinkBox.Text.Trim()==""){ r=x; break; } }
    first=false; if(r==null){ r=AddRow(new Item()); rows=Rows(); i=rows.Count; }
    r.LinkBox.Text=u;
   }
   Log("링크 "+urls.Count+"개 추가");
  }

  // ── 검수
  async Task CheckRow(RowView r){
   var it=r.It; Commit();
   string link=Tx.Clean(r.LinkBox.Text);
   if(it.DocPath==""||link==""){ it.State="오류"; it.Error=it.DocPath==""?"원고 파일을 넣어 주세요.":"블로그 링크를 넣어 주세요."; r.RefreshView(); if(sel==r) LoadDetail(); UpdateChips(); return; }
   it.State="검수 중"; r.RefreshView(); string doc=it.DocPath; status.Text=r.NoLbl.Text+"번 검수 중…";
   try {
    var res=await Task.Run(()=>{ var ms=Manuscript.Load(doc); var post=Post.Fetch(link); return Tuple.Create(post,Checker.Compare(ms,post)); });
    it.Post=res.Item1; it.Res=res.Item2; it.Final=it.Res.Summary(); it.State=it.Res.Fix.Count==0?"일치":"불일치"; it.Error=""; it.CheckedKey=doc+"|"+link;
    Log(r.NoLbl.Text+". "+r.NameBox.Text+" → "+it.State+(it.Res.Fix.Count>0?" ("+it.Res.Fix.Count+"건)":""));
   } catch(Exception ex){
    var e=ex is AggregateException&&ex.InnerException!=null?ex.InnerException:ex;
    it.State="오류"; it.Res=null; it.Post=null; it.Final=null; it.Error=e is WebException?"블로그 글을 불러오지 못했어요. 인터넷 연결과 링크를 확인해 주세요. ("+e.Message+")":e.Message;
    Log(r.NoLbl.Text+". "+r.NameBox.Text+" 오류: "+it.Error);
   }
   r.RefreshView(); if(sel==r) LoadDetail(); UpdateChips();
  }
  async Task RunAll(){
   if(busy) return; Commit();
   var todo=Rows().Where(r=>r.It.DocPath!=""&&Tx.Clean(r.LinkBox.Text)!="").ToList();
   if(todo.Count==0){ MessageBox.Show("원고와 링크를 모두 넣은 행이 없어요.","원고 검수기"); return; }
   busy=true; SetBusy(true);
   try { for(int i=0;i<todo.Count;i++){ await CheckRow(todo[i]); if(i<todo.Count-1) await Task.Delay(500); } }
   finally { busy=false; SetBusy(false); }
  }
  void UpdateChips(){
   var rows=Rows(); chipAll.Text="총 "+rows.Count; chipOk.Text="일치 "+rows.Count(r=>r.It.State=="일치"); chipBad.Text="불일치 "+rows.Count(r=>r.It.State=="불일치");
   int err=rows.Count(r=>r.It.State=="오류"), wait=rows.Count(r=>r.It.State=="대기");
   status.Text="원고 "+rows.Count(r=>r.It.DocPath!="")+"개 · 링크 "+rows.Count(r=>r.LinkBox.Text.Trim()!="")+"개"+(wait>0?" · 검수 전 "+wait:"")+(err>0?" · 오류 "+err:"");
  }

  // ── 수정사항 편집
  void UpdateDetailTitle(){
   if(sel==null){ detailInfo.Text=""; return; }
   var it=sel.It; string nm=sel.NameBox.Text.Trim();
   string info=sel.NoLbl.Text+"번"+(nm.Length>0?" · "+nm:"");
   if(it.Post!=null) info+="  |  "+Tx.Cut(it.Post.Title,40)+" · "+it.Post.Date+(it.Post.Nick.Length>0?" · "+it.Post.Nick:"");
   detailInfo.Text=info;
  }
  void LoadDetail(){
   loading=true;
   try {
    rtb.Clear(); notes.Items.Clear(); UpdateDetailTitle();
    openPost.Enabled=sel!=null&&sel.It.Post!=null;
    if(sel==null){ rtb.ReadOnly=true; return; }
    var it=sel.It;
    if(it.Final!=null){ rtb.ReadOnly=false; WriteRich(it.Final); if(it.Res!=null) foreach(var n in it.Res.Notes) notes.Items.Add(new NoteItem{Raw=n}); }
    else { rtb.ReadOnly=true; rtb.ForeColor=Muted; rtb.Text=it.State=="오류"?it.Error:it.State=="검수 중"?"검수 중이에요…":"원고와 링크를 넣고 검수하기를 누르면 여기에 수정사항이 나와요.\n여기서 문구를 바로 고칠 수 있고, 빨간 글씨는 엑셀에서도 빨간색으로 저장돼요."; rtb.ForeColor=Ink; }
   } finally { loading=false; }
  }
  void WriteRich(string s){
   bool old=loading; loading=true; rtb.Clear(); bool red=false; var cur=new StringBuilder();
   Action flush=()=>{ if(cur.Length==0) return; rtb.SelectionStart=rtb.TextLength; rtb.SelectionLength=0; rtb.SelectionColor=red?RedText:Ink; rtb.SelectedText=cur.ToString(); cur.Clear(); };
   foreach(char c in (s??"").Replace("\r\n","\n")){ if(c==Tx.RO){ flush(); red=true; } else if(c==Tx.RC){ flush(); red=false; } else cur.Append(c); }
   flush(); rtb.SelectionStart=0; rtb.SelectionLength=0; rtb.SelectionColor=Ink; loading=old;
  }
  string ReadRich(){
   string text=rtb.Text; var sb=new StringBuilder(); bool red=false; int ss=rtb.SelectionStart, sl=rtb.SelectionLength;
   SendMessage(rtb.Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
   try {
    for(int i=0;i<text.Length;i++){ rtb.Select(i,1); var c=rtb.SelectionColor; bool r=c.R>170&&c.G<90&&c.B<90&&text[i]!='\n'; if(r!=red){ sb.Append(r?Tx.RO:Tx.RC); red=r; } sb.Append(text[i]); }
    if(red) sb.Append(Tx.RC); rtb.Select(ss,sl);
   } finally { SendMessage(rtb.Handle,0x000B,new IntPtr(1),IntPtr.Zero); rtb.Invalidate(); }
   return sb.ToString().Replace(Tx.RO.ToString()+Tx.RC,"");
  }
  void Commit(){
   if(sel==null||sel.IsDisposed||loading||rtb.ReadOnly) return;
   var it=sel.It; string s=ReadRich().TrimEnd('\n',' ');
   if(it.Final==s) return; it.Final=s;
   if(it.Res!=null){ string plain=Tx.Clean(Tx.Plain(s)); it.State=plain.Length==0||plain=="이상 없음"?"일치":"불일치"; sel.RefreshView(); UpdateChips(); }
  }
  void PaintSel(bool red){ if(sel==null||rtb.ReadOnly) return; if(rtb.SelectionLength==0){ MessageBox.Show("색을 바꿀 글자를 먼저 드래그해 주세요.","원고 검수기"); return; } rtb.SelectionColor=red?RedText:Ink; Commit(); rtb.Focus(); }
  void AddToFinal(string item){
   if(sel==null||rtb.ReadOnly) return; Commit();
   var lines=(sel.It.Final??"").Split('\n').Select(x=>Regex.Replace(x,"^[\u2460-\u2473]\\s*","")).Where(x=>Tx.Clean(Tx.Plain(x)).Length>0&&Tx.Clean(Tx.Plain(x))!="이상 없음").ToList();
   lines.Add(item);
   WriteRich(lines.Count==1?lines[0]:String.Join("\n",lines.Select((x,i)=>Tx.Circled(i)+" "+x))); Commit();
  }

  // ── 엑셀 내보내기
  void Export(){
   if(busy) return; Commit();
   var rows=Rows();
   var waiting=rows.Where(r=>r.It.Final==null&&r.It.DocPath!=""&&Tx.Clean(r.LinkBox.Text)!="").ToList();
   if(waiting.Count>0){
    var a=MessageBox.Show("아직 검수하지 않은 행이 "+waiting.Count+"개 있어요. 먼저 전체 검수를 할까요?\n\n예: 전체 검수 후 저장 / 아니요: 검수한 행만 저장","엑셀 내보내기",MessageBoxButtons.YesNoCancel);
    if(a==DialogResult.Cancel) return;
    if(a==DialogResult.Yes){ var t=RunAll(); t.ContinueWith(_=>BeginInvoke(new Action(SaveExcel)),TaskScheduler.FromCurrentSynchronizationContext()); return; }
   }
   SaveExcel();
  }
  void SaveExcel(){
   Commit();
   var done=Rows().Where(r=>r.It.Final!=null&&r.It.Post!=null).ToList();
   if(done.Count==0){ MessageBox.Show("저장할 검수 결과가 없어요. 먼저 검수해 주세요.","엑셀 내보내기"); return; }
   var sheets=done.GroupBy(r=>Xlsx.PostDate(r.It.Post.Date)).OrderBy(g=>g.Key).Select(g=>new KeyValuePair<string,List<XRow>>(Xlsx.SheetName(g.Key),g.Select((r,i)=>new XRow{No=i+1,Name=r.NameBox.Text.Trim(),Url=r.It.Post.Url,Text=r.It.Final}).ToList())).ToList();
   string first=done[0].It.DocPath, stem=System.IO.Path.GetFileNameWithoutExtension(first).Split('_')[0].Trim();
   stem=stem.Contains("원고")?stem.Replace("원고","검수"):"원고 검수";
   using(var d=new SaveFileDialog{Filter="엑셀 파일 (*.xlsx)|*.xlsx",FileName=stem+"_"+DateTime.Now.ToString("yyMMdd")+".xlsx",InitialDirectory=System.IO.Path.GetDirectoryName(first)}){
    if(d.ShowDialog(this)!=DialogResult.OK) return;
    try { Xlsx.Save(d.FileName,sheets); }
    catch(IOException ex){ MessageBox.Show("저장하지 못했어요. 같은 이름의 엑셀 파일이 열려 있으면 닫고 다시 해 주세요.\n("+ex.Message+")","엑셀 내보내기"); return; }
    Log("엑셀 저장: "+d.FileName+" ("+done.Count+"건, 시트 "+sheets.Count+"개)");
    if(MessageBox.Show(done.Count+"건을 시트 "+sheets.Count+"개("+String.Join(", ",sheets.Select(x=>x.Key))+")로 저장했어요. 바로 열까요?","엑셀 내보내기",MessageBoxButtons.YesNo)==DialogResult.Yes) Open(d.FileName);
   }
  }

  // ── 기타
  void Help(){
   MessageBox.Show("1. 원고(.docx)를 '파일 넣기'로 넣거나 창에 끌어다 놓아요. 여러 개를 한 번에 넣으면 행이 자동으로 늘어나요.\n"
    +"2. 블로그 글 링크를 넣어요. 링크 여러 줄을 복사해 링크 칸에 붙여넣으면 아래 행으로 나눠 들어가요.\n"
    +"3. 검수하기(한 행) 또는 전체 검수를 눌러요. 결과는 일치/불일치로 나와요.\n"
    +"4. 아래 '수정사항'에서 문구를 고칠 수 있어요. 글자를 드래그해 빨간 글씨로 바꾸면 엑셀에서도 빨간색이에요.\n"
    +"   오른쪽 '참고'는 확실하지 않은 차이예요. 더블클릭하면 수정사항에 추가돼요.\n"
    +"5. 엑셀 내보내기를 누르면 게시일별 시트(예: 26.10.2)에 순서·이름·URL·수정사항으로 저장돼요.\n\n"
    +"원고에서 읽는 표시\n· 빨간 취소선: 삭제할 부분 → 블로그에 남아 있으면 '삭제 미반영'\n· 노란 형광펜: 수정한 문구 → 블로그에 없으면 '수정 미반영'\n· 하늘색 형광펜·빨간 안내문(※…): 검수 메모 → 블로그에 남아 있으면 지적\n· 표 안 검정 취소선: 정가 취소선 → 블로그 표에 없으면 지적\n· 링크, 태그, 깨진 글자(à 등), 같은 단어 두 번도 함께 확인해요.","사용 방법");
  }
  void Open(string target){ try{ Process.Start(new ProcessStartInfo(target){UseShellExecute=true}); }catch(Exception e){ MessageBox.Show(e.Message,"열기 실패"); } }
  void Log(string text){ if(IsDisposed) return; if(InvokeRequired){ BeginInvoke(new Action<string>(Log),text); return; } logs.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine); }

  // ── 미리보기 PNG (개발 확인용)
  public void SavePreview(string path){
   var rows=Rows();
   var demo=new[]{
    new{N="예시 블로거 A",F="10월 캠페인 원고_예시 블로거 A_검수.docx",L="https://blog.naver.com/example_a/224400000001",S="불일치",T="① 태그 오타: '"+Tx.Red("#가을여행추천")+"' → '#가을여행코스추천'\n② 표 정가 취소선 없음, 수치는 원고와 동일"},
    new{N="예시 블로거 B",F="10월 캠페인 원고_예시 블로거 B_검수.docx",L="https://blog.naver.com/example_b/224400000002",S="일치",T="이상 없음"},
    new{N="예시 블로거 C",F="10월 캠페인 원고_예시 블로거 C_검수.docx",L="https://blog.naver.com/example_c/224400000003",S="불일치",T="삭제 미반영: '할인 쿠폰 "+Tx.Red("또는 적립금")+" 5천 원' → '또는 적립금' 삭제"}};
   for(int i=0;i<demo.Length;i++){
    var r=i<rows.Count?rows[i]:AddRow(new Item()); var it=r.It;
    it.DocPath=@"C:\원고\"+demo[i].F; r.NameBox.Text=demo[i].N; r.LinkBox.Text=demo[i].L;
    it.Post=new Post{Url=demo[i].L,Title="예시 블로그 글 제목",Date="2026. 10. 2. 19:19",Nick=demo[i].N};
    it.Res=new Result(); if(i==0) it.Res.Notes.Add("원고에 없는 태그: #예시태그"); it.Final=demo[i].T; it.State=demo[i].S; it.CheckedKey=it.DocPath+"|"+demo[i].L; r.RefreshView();
   }
   UpdateChips(); ShowInTaskbar=false; Opacity=0; Show(); Application.DoEvents(); PerformLayout(); FitRows();
   sel=null; Select(Rows()[0]); ActiveControl=null; Application.DoEvents();
   using(var bmp=new Bitmap(Width,Height)){
    DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));
    using(var g=Graphics.FromImage(bmp)){   // RichTextBox는 DrawToBitmap에 그려지지 않아 직접 그린다
     var pt=rtb.Parent.PointToScreen(rtb.Location); var origin=PointToScreen(Point.Empty); int ox=pt.X-origin.X+(Width-ClientSize.Width)/2, oy=pt.Y-origin.Y+(Height-ClientSize.Height-(Width-ClientSize.Width)/2);
     float y=oy; foreach(var line in rtb.Text.Split('\n')){ float x=ox; int at=rtb.Text.IndexOf(line,StringComparison.Ordinal);
      for(int i=0;i<line.Length;i++){ rtb.Select(at+i,1); var c=rtb.SelectionColor; string ch=line[i].ToString(); var sz=TextRenderer.MeasureText(g,ch,rtb.Font,Size.Empty,TextFormatFlags.NoPadding); TextRenderer.DrawText(g,ch,rtb.Font,new Point((int)x,(int)y),c,TextFormatFlags.NoPadding); x+=sz.Width; }
      y+=rtb.Font.Height+3; }
    }
    bmp.Save(path,ImageFormat.Png);
   }
   Close();
  }
 }

 static class Program {
  static void Require(bool ok,string what){ if(!ok) throw new Exception("실패: "+what); }
  static void SelfTest(string path){
   var lines=new List<string>();
   try {
    Require(Manuscript.NameFromFile(@"C:\a\10월 캠페인 원고_스팟프로모션_예시 블로거_검수.docx")=="예시 블로거","원고 파일 이름에서 닉네임");
    Require(Manuscript.NameFromFile("원고_예시_최종.docx")=="예시","_최종 제거");
    string id,no; Require(Post.Ids("https://m.blog.naver.com/PostView.naver?blogId=example_a&logNo=224400000001",out id,out no)&&id=="example_a"&&no=="224400000001","모바일 주소");
    Require(Post.Ids("https://blog.naver.com/example_a/224400000001?trackingCode=x",out id,out no)&&no=="224400000001","PC 주소");
    Require(Checker.UrlKey("https://example.com/%C2%A0")==Checker.UrlKey("https://example.com"),"링크 끝 공백 무시");
    Require(Checker.UrlKey("https://youtu.be/abcdEFGH123")==Checker.UrlKey("https://www.youtube.com/watch?v=abcdEFGH123"),"유튜브 주소");
    var h=Checker.Hunks("가입은 공식 홈페이지에서 디렉트몰에 하면 됩니다","가입은 공식 홈페이지에서 다이렉트몰에 하면 됩니다"); Require(h.Count==1&&h[0].APart=="디렉트몰에","단어 차이");
    Require(Checker.Hunks("2만원 혜택","2만 원 혜택").Count==0,"띄어쓰기만 다르면 무시");
    Require(Xlsx.SheetName(Xlsx.PostDate("2026. 10. 2. 19:19"))=="26.10.2","시트 이름");
    string x=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wongo_selftest.xlsx");
    Xlsx.Save(x,new List<KeyValuePair<string,List<XRow>>>{new KeyValuePair<string,List<XRow>>("26.10.2",new List<XRow>{new XRow{No=1,Name="예시",Url="https://blog.naver.com/example_a/1",Text="① 태그 오타: '"+Tx.Red("#a")+"' → '#b'\n② 표"}})});
    using(var z=ZipFile.OpenRead(x)) Require(z.GetEntry("xl/worksheets/sheet1.xml")!=null&&z.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!=null,"엑셀 파일 구조");
    using(var f=new MainForm()){ f.CreateControl(); Require(f.Controls.Count>0,"화면 생성"); }
    lines.Add("PASS");
   } catch(Exception e){ lines.Add("FAIL: "+e); Environment.ExitCode=1; }
   File.WriteAllLines(path,lines,new UTF8Encoding(true));
  }
  // 원고 폴더와 저장해 둔 블로그 HTML 폴더로 한꺼번에 대조 (개발 확인용)
  static void Batch(string docsDir,string htmlDir,string outTxt,string outXlsx){
   var sb=new StringBuilder(); var rows=new List<Tuple<DateTime,XRow>>();
   foreach(var f in Directory.GetFiles(docsDir,"*.docx").OrderBy(x=>x)){
    var m=Manuscript.Load(f); string html=System.IO.Path.Combine(htmlDir,m.Name+".html");
    sb.AppendLine("######## "+m.Name+"  (제목: "+m.Title+", 표 "+m.Tables.Count+", 사진 "+m.Images+")");
    if(!File.Exists(html)){ sb.AppendLine("  (html 없음)"); continue; }
    var p=Post.Parse(File.ReadAllText(html,Encoding.UTF8)); if(p.BlogId!="") p.Tags=Post.FetchTags(p.BlogId,p.LogNo); p.Finish();
    var r=Checker.Compare(m,p);
    sb.AppendLine("[수정사항]"); sb.AppendLine(r.Summary());
    if(r.Notes.Count>0){ sb.AppendLine("[참고]"); foreach(var n in r.Notes) sb.AppendLine("  - "+n); }
    sb.AppendLine();
    rows.Add(Tuple.Create(Xlsx.PostDate(p.Date),new XRow{Name=m.Name,Url=p.Url,Text=r.Summary()}));
   }
   File.WriteAllText(outTxt,sb.ToString(),new UTF8Encoding(true));
   if(outXlsx!=null) Xlsx.Save(outXlsx,rows.GroupBy(x=>x.Item1).OrderBy(g=>g.Key).Select(g=>new KeyValuePair<string,List<XRow>>(Xlsx.SheetName(g.Key),g.Select((x,i)=>{ x.Item2.No=i+1; return x.Item2; }).ToList())).ToList());
  }
  [STAThread] static void Main(string[] args){
   ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   try {
    if(args.Length>=2&&args[0]=="--make-icon"){ Logo.SaveIco(args[1]); return; }
    if(args.Length>=2&&args[0]=="--self-test"){ SelfTest(args[1]); return; }
    if(args.Length>=2&&args[0]=="--preview"){ using(var f=new MainForm()) f.SavePreview(args[1]); return; }
    if(args.Length>=4&&args[0]=="--batch"){ Batch(args[1],args[2],args[3],args.Length>=5?args[4]:null); return; }
    if(args.Length>=4&&args[0]=="--check"){ var m=Manuscript.Load(args[1]); var p=Post.Fetch(args[2]); var r=Checker.Compare(m,p); File.WriteAllText(args[3],p.Url+"\n"+p.Date+"\n[수정사항]\n"+r.Summary()+"\n[참고]\n"+String.Join("\n",r.Notes),new UTF8Encoding(true)); return; }
   } catch(Exception e){ if(args.Length>=2) File.WriteAllText(args[args.Length-1]+".error.txt",e.ToString(),new UTF8Encoding(true)); Environment.ExitCode=1; return; }
   Application.Run(new MainForm());
  }
 }
}
