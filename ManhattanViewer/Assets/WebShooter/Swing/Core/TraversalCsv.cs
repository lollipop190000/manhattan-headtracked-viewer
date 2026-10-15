using System.Collections.Generic;
using System.IO;
namespace WebShooter.Swing
{
    public static class TraversalCsv
    {
        public static string Quote(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
        public static IEnumerable<string[]> Read(TextReader reader)
        {
            var fields=new List<string>();var value=new System.Text.StringBuilder();bool quoted=false;
            for(int c=reader.Read();c>=0;c=reader.Read())
            {
                char ch=(char)c;
                if(ch=='"')
                {
                    if(quoted && reader.Peek()=='"'){reader.Read();value.Append('"');}
                    else quoted=!quoted;
                }
                else if(!quoted && (ch==',' || ch=='\n'))
                {
                    fields.Add(value.ToString().TrimEnd('\r'));value.Clear();
                    if(ch=='\n'){yield return fields.ToArray();fields.Clear();}
                }
                else value.Append(ch);
            }
            if(value.Length>0 || fields.Count>0){fields.Add(value.ToString());yield return fields.ToArray();}
        }
    }
}
