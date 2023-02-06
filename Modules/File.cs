using System.IO;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace RuleChecker.Modules.File
{
    [RuleType("file-filter", "Filter files using advanced filter settings")]
    public class FileFilterRule : Rule
    {
        [RuleField("Defines the rulesets that will be run if the filter matches", true)]
        public string then = null!;

        //Filter
        [RuleField("Filter by name (Regex)")]
        public string name = null!;

        [RuleField("Filter by path (Regex)")]
        public string path = null!;

        [RuleField("Check if the path is a file")]
        public bool file = false;

        [RuleField("Check if the path is a folder")]
        public bool folder = false;

        [RuleField("Defines if the filter will be inverse")]
        [DefaultValue(false)]
        public bool inverse = false;

        //Children
        [RuleField("Iterate over children files (Folder Only)")]
        public bool childrenfiles = false;
        [RuleField("Iterate over children folders (Folder Only)")]
        public bool childrenfolders = false;

        //Variables
        [RuleField("Defines the output name variable")]
        [DefaultValue("file-name")]
        public string varname = "file-name";
        [RuleField("Defines the output path variable")]
        [DefaultValue("file-path")]
        public string varpath = "file-path";

        public bool Filter(string path)
        {
            bool f = _Filter(path);
            return inverse ? !f : f;
        }

        private bool _Filter(string path)
        {
            string name = Path.GetFileName(path);

            if(folder && !Directory.Exists(path)) return false;
            if(file && !System.IO.File.Exists(path)) return false;
            if(this.name != null && !Regex.IsMatch(name, this.name)) return false;
            if(this.path != null && !Regex.IsMatch(path, this.path)) return false;

            return true;
        }

        protected override bool OnTest(RuleTest test, object data)
        {
            string path = (data as string)!; 

            #region Then
            if(Directory.Exists(path))
            {
                if(childrenfiles)
                {
                    foreach(string file in Directory.GetFiles(path))
                        CallNext(file, test);
                }
                
                if(childrenfolders)
                {
                    foreach(string dir in Directory.GetDirectories(path))
                        CallNext(dir, test);
                }
            }
            
            if(!childrenfiles && !childrenfolders)
                CallNext(path, test);
            #endregion

            return true;
        }

        public void CallNext(string path, RuleTest test)
        {
            if(Filter(path))
            {
                test.engine.SetVariable(varpath, path);
                test.engine.SetVariable(varname, Path.GetFileName(path));
                Call(then, test, path);
            }
        }
    }
}