using System;
using System.Globalization;
namespace Manhattan.TraversalLab
{
    public static class LabArguments
    {
        public static string Get(string key,string fallback)
        {var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]==key)return args[i+1];return fallback;}
        public static int Int(string key,int fallback)=>int.TryParse(Get(key,""),out int n)?n:fallback;
        public static float Float(string key,float fallback)=>float.TryParse(Get(key,""),NumberStyles.Float,CultureInfo.InvariantCulture,out float n)?n:fallback;
        public static bool Has(string key)=>Array.IndexOf(Environment.GetCommandLineArgs(),key)>=0;
    }
}
