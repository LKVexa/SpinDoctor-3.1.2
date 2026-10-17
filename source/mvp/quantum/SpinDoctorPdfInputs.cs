using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace QuettaQuantum
{
    // Bounded data extraction only. PDF actions, scripts, links and document
    // instructions are never executed. Programs must use an explicit format.
    public static class SpinDoctorPdfInputs
    {
        public sealed class Extraction { public string Filename, Content, Method; public int TextStreams; }
        private static readonly Encoding Latin = Encoding.GetEncoding(28591);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false,true);
        private static Match MatchPdf(string text,string pattern) { return Regex.Match(text,pattern,RegexOptions.None,TimeSpan.FromSeconds(2)); }
        private static Match MatchPdf(string text,string pattern,RegexOptions options) { return Regex.Match(text,pattern,options,TimeSpan.FromSeconds(2)); }
        private static bool IsMatchPdf(string text,string pattern) { return Regex.IsMatch(text,pattern,RegexOptions.None,TimeSpan.FromSeconds(2)); }
        public static Extraction Extract(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > 4194304 || Latin.GetString(bytes,0,5) != "%PDF-") throw new ArgumentException("PDF inputs require a valid PDF header and at most4MiB.");
            string source = Latin.GetString(bytes);
            if (!IsMatchPdf(source,@"^%PDF-[12]\.[0-9](?:\r|\n)") || !source.TrimEnd(' ','\t','\r','\n','\0').EndsWith("%%EOF",StringComparison.Ordinal)) throw new ArgumentException("PDF version/header or final EOF marker is missing.");
            if (IsMatchPdf(source,@"/Encrypt\b")) throw new ArgumentException("Encrypted PDFs cannot be imported. Export an unencrypted tagged equation/circuit PDF.");
            Dictionary<int,string> objects = new Dictionary<int,string>();
            Regex objectHeader=new Regex(@"(?m)(\d+)\s+\d+\s+obj\b",RegexOptions.None,TimeSpan.FromSeconds(2));int objectSearch=0;while(objectSearch<source.Length)
            {
                Match m=objectHeader.Match(source,objectSearch);if(!m.Success)break;int bodyStart=m.Index+m.Length;
                if (objects.Count >= 20000) throw new ArgumentException("PDF object count exceeds the bounded reader.");
                int bodyEnd=source.IndexOf("endobj",bodyStart,StringComparison.Ordinal);if(bodyEnd<0)throw new ArgumentException("PDF object terminator is missing.");
                string prefix=source.Substring(bodyStart,bodyEnd-bodyStart);Match streamHeader=MatchPdf(prefix,@"\bstream(?=\r|\n)");if(streamHeader.Success){Match len=MatchPdf(prefix.Substring(0,streamHeader.Index),@"/Length\s+(\d+)\s*(?:/|>>|\r|\n)");int exact;if(len.Success&&Int32.TryParse(len.Groups[1].Value,out exact)){int dataStart=bodyStart+streamHeader.Index+6;if(dataStart<source.Length&&source[dataStart]=='\r')dataStart++;if(dataStart<source.Length&&source[dataStart]=='\n')dataStart++;if(exact<0||exact>source.Length-dataStart)throw new ArgumentException("PDF stream length exceeds source bytes.");bodyEnd=source.IndexOf("endobj",dataStart+exact,StringComparison.Ordinal);if(bodyEnd<0)throw new ArgumentException("PDF streamed object terminator is missing.");}}
                int objectId;if(!Int32.TryParse(m.Groups[1].Value,out objectId)||objects.ContainsKey(objectId))throw new ArgumentException("PDF object IDs must be unique bounded integers. Flatten incremental revisions before import.");
                objects.Add(objectId, source.Substring(bodyStart,bodyEnd-bodyStart));objectSearch=bodyEnd+6;
            }
            if(objects.Count==0)throw new ArgumentException("PDF contains no readable objects. Compressed object streams are not supported by this bounded program reader.");
            List<Extraction> attached = new List<Extraction>();
            long decodedBytes=0;
            foreach (string body in objects.Values)
            {
                if (!IsMatchPdf(body,@"/Type\s*/Filespec\b")) continue;
                Match file = MatchPdf(body,@"/F\s*\(((?:\\.|[^)\\])*)\)"); Match reference = MatchPdf(body,@"/EF\s*<<.*?/F\s+(\d+)\s+\d+\s+R",RegexOptions.Singleline);
                if (!file.Success || !reference.Success) continue;
                string name = DecodeLiteral(file.Groups[1].Value);
                if (Path.GetFileName(name) != name || !Supported(name)) continue;
                string embedded; if (!objects.TryGetValue(Int32.Parse(reference.Groups[1].Value),out embedded)) throw new ArgumentException("PDF embedded program reference is missing.");
                attached.Add(new Extraction {Filename=name,Content=Utf8.GetString(StreamBytes(embedded,ref decodedBytes)),Method="embedded UTF-8 program",TextStreams=0});
            }
            if (attached.Count > 1) throw new ArgumentException("PDF has multiple supported embedded programs. Export one program per PDF.");
            if (attached.Count == 1) return attached[0];
            StringBuilder text = new StringBuilder(); int streams=0;
            foreach (string body in objects.Values)
            {
                if (IsMatchPdf(body,@"/Subtype\s*/Image\b") || body.IndexOf("stream",StringComparison.Ordinal)<0) continue;
                byte[] data;
                try { data=StreamBytes(body,ref decodedBytes); } catch (ArgumentException) { continue; }
                string decoded=Latin.GetString(data);
                int textObjects;foreach(string literal in TextLiterals(decoded,out textObjects)){text.Append(literal).Append('\n');if(text.Length>2097152)throw new ArgumentException("PDF extracted text exceeds2MiB.");}streams+=textObjects;
            }
            string content = text.ToString();
            List<Extraction> tagged=new List<Extraction>();
            foreach (string type in new string[] {"JSON","QASM","TXT","CSV"})
            {
                string begin="SPINDOCTOR-"+type+"-BEGIN",end="SPINDOCTOR-"+type+"-END";
                int search=0;while(search<content.Length){int at=content.IndexOf(begin,search,StringComparison.Ordinal);if(at<0)break;int stop=content.IndexOf(end,at+begin.Length,StringComparison.Ordinal);if(stop<0)throw new ArgumentException("PDF tagged program is missing its END marker.");tagged.Add(new Extraction {Filename="pdf-program."+type.ToLowerInvariant(),Content=content.Substring(at+begin.Length,stop-at-begin.Length).Trim(),Method="explicit tagged ASCII text",TextStreams=streams});search=stop+end.Length;}
            }
            if(tagged.Count>1)throw new ArgumentException("PDF has multiple tagged programs. Export one explicit program per PDF.");if(tagged.Count==1)return tagged[0];
            throw new ArgumentException("No explicit quantum program found. PDF must embed one UTF-8 JSON/QASM/TXT/CSV file or contain SPINDOCTOR-JSON-BEGIN/END (or QASM/TXT/CSV) text blocks. Scanned images, prose and untagged equations require transcription; no OCR or guessed execution is performed.");
        }
        private static bool Supported(string name) { string x=Path.GetExtension(name).ToLowerInvariant();return x==".json"||x==".qasm"||x==".txt"||x==".csv"; }
        private static byte[] StreamBytes(string body,ref long decodedBytes)
        {
            Match stream=MatchPdf(body,@"\bstream(?=\r|\n)");int at=stream.Success?stream.Index:-1;if(at<0)throw new ArgumentException("PDF stream missing.");
            int start=at+6; if(start<body.Length&&body[start]=='\r')start++;if(start<body.Length&&body[start]=='\n')start++;
            int end=body.LastIndexOf("endstream",StringComparison.Ordinal);if(end<start)throw new ArgumentException("PDF stream terminator missing.");
            int length=end-start; Match declared=MatchPdf(body.Substring(0,at),@"/Length\s+(\d+)\s*(?:/|>>|\r|\n)");
            int exact;if(declared.Success){if(!Int32.TryParse(declared.Groups[1].Value,out exact)||exact>length)throw new ArgumentException("PDF declared stream length is invalid.");length=exact;string tail=body.Substring(start+length).TrimStart('\r','\n');if(!tail.StartsWith("endstream",StringComparison.Ordinal))throw new ArgumentException("PDF stream length does not reach its terminator.");}else{if(length>0&&body[start+length-1]=='\n')length--;if(length>0&&body[start+length-1]=='\r')length--;}
            byte[] data=Latin.GetBytes(body.Substring(start,length));string dict=body.Substring(0,at);
            if(dict.IndexOf("/Filter",StringComparison.Ordinal)<0){BudgetDecoded(ref decodedBytes,data.Length);if(data.Length>2097152)throw new ArgumentException("PDF program/text stream exceeds2MiB.");return data;}
            Match filter=MatchPdf(dict,@"/Filter\s*(/\w+|\[[^]]*\])");if(!filter.Success||!IsMatchPdf(filter.Groups[1].Value,@"^(?:/FlateDecode|\[\s*/FlateDecode\s*\])\z"))throw new ArgumentException("PDF stream filter unsupported. Use one FlateDecode filter or an uncompressed embedded program.");
            if(dict.IndexOf("/DecodeParms",StringComparison.Ordinal)>=0&&!IsMatchPdf(dict,@"/DecodeParms\s+null\b"))throw new ArgumentException("PDF stream predictors/decode parameters are unsupported.");
            if(data.Length<6)throw new ArgumentException("Invalid compressed PDF stream.");
            if((data[0]&15)!=8||(data[0]>>4)>7||((data[0]<<8)+data[1])%31!=0||(data[1]&32)!=0)throw new ArgumentException("Invalid/unsupported zlib PDF stream header.");
            using(MemoryStream input=new MemoryStream(data,2,data.Length-6))using(DeflateStream decompressor=new DeflateStream(input,CompressionMode.Decompress))using(MemoryStream output=new MemoryStream())
            {
                byte[] buffer=new byte[8192];int n;while((n=decompressor.Read(buffer,0,buffer.Length))>0){BudgetDecoded(ref decodedBytes,n);if(output.Length+n>2097152)throw new ArgumentException("Decompressed PDF stream exceeds2MiB.");output.Write(buffer,0,n);}byte[] result=output.ToArray();uint a=1,b=0;foreach(byte value in result){a=(a+value)%65521;b=(b+a)%65521;}uint expected=((uint)data[data.Length-4]<<24)|((uint)data[data.Length-3]<<16)|((uint)data[data.Length-2]<<8)|data[data.Length-1];if(((b<<16)|a)!=expected)throw new ArgumentException("PDF compressed stream checksum mismatch.");return result;
            }
        }
        private static void BudgetDecoded(ref long decodedBytes,int count)
        {
            decodedBytes+=count;if(decodedBytes>16777216)throw new InvalidDataException("PDF aggregate decoded-stream budget exceeds16MiB. Export a smaller program-only PDF.");
        }
        private static List<string> TextLiterals(string decoded,out int objects)
        {
            List<string> result=new List<string>();objects=0;bool inText=false;int p=0;
            while(p<decoded.Length){char c=decoded[p];if(Char.IsWhiteSpace(c)){p++;continue;}if(c=='%'){while(p<decoded.Length&&decoded[p]!='\r'&&decoded[p]!='\n')p++;continue;}if(c=='/'){p++;while(p<decoded.Length&&!Char.IsWhiteSpace(decoded[p])&&"()[]<>{}/%".IndexOf(decoded[p])<0)p++;continue;}if(c=='('){int begin=++p,depth=1;while(p<decoded.Length&&depth>0){c=decoded[p];if(c=='\\'){p+=2;continue;}if(c=='('){if(++depth>32)throw new ArgumentException("PDF literal nesting exceeds32.");}else if(c==')')depth--;p++;}if(depth!=0)throw new ArgumentException("PDF text literal is unclosed.");if(inText)result.Add(DecodeLiteral(decoded.Substring(begin,p-begin-1)));continue;}if(c=='<'&&p+1<decoded.Length&&decoded[p+1]!='<'){p++;while(p<decoded.Length&&decoded[p]!='>')p++;if(p<decoded.Length)p++;continue;}if("[]<>{}".IndexOf(c)>=0){p++;continue;}int start=p;while(p<decoded.Length&&!Char.IsWhiteSpace(decoded[p])&&"()[]<>{}/%".IndexOf(decoded[p])<0)p++;if(p==start){p++;continue;}string token=decoded.Substring(start,p-start);if(token=="BT"){inText=true;objects++;}else if(token=="ET")inText=false;}return result;
        }
        private static string DecodeLiteral(string raw)
        {
            StringBuilder result=new StringBuilder();for(int i=0;i<raw.Length;i++)
            {
                char c=raw[i];if(c!='\\'){result.Append(c);continue;}if(++i>=raw.Length)break;c=raw[i];
                if(c=='n')result.Append('\n');else if(c=='r')result.Append('\r');else if(c=='t')result.Append('\t');else if(c=='\r'||c=='\n'){if(c=='\r'&&i+1<raw.Length&&raw[i+1]=='\n')i++;}
                else if(c>='0'&&c<='7'){int value=c-'0';for(int j=0;j<2&&i+1<raw.Length&&raw[i+1]>='0'&&raw[i+1]<='7';j++)value=value*8+(raw[++i]-'0');result.Append((char)value);}else result.Append(c);
            }return result.ToString();
        }
    }
}
