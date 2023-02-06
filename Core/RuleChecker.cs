using System;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Tsukikage.SharpJson;

namespace RuleChecker
{
    #region Types
    public class RuleEngine
    {
        private static Regex PATTERN_ARGS = new Regex("('[^'\\\\]*(\\\\.[^'\\\\]*)*')|([^\\s]*)");
        private static Regex PATTERN_VARS = new Regex("{([^}]*)}");

        private Dictionary<string, Type> _ruleTypes = new Dictionary<string, Type>();
        private Dictionary<string, string> _variables = new Dictionary<string, string>();
        private Dictionary<string, RuleSet> _ruleSets = new Dictionary<string, RuleSet>();

        #region Test
        public RuleTest Test(object data, string main = ".")
        {
            RuleTest test = new RuleTest(this);
            GetRuleSet(main)?.Test(test, data);
            return test;
        }
        #endregion

        #region Rule Sets
        public RuleEngine With(string key, RuleSet set)
        {
            _ruleSets[key] = set;
            return this;
        }

        public RuleSet GetRuleSet(string key)
        {
            if (_ruleSets.ContainsKey(key))
                return _ruleSets[key];

            return null!;
        }
        #endregion

        #region Rule Types
        public void RegisterRuleType(string key, Type type)
        {
            if (!type.IsSubclassOf(typeof(Rule)))
                throw new Exception($"Type '{type}' must be subtype of 'Rule'");

            _ruleTypes[key] = type;
        }

        public Type GetRuleType(string key)
        {
            if (_ruleTypes.ContainsKey(key))
                return _ruleTypes[key];

            return null!;
        }

        public void LoadRuleTypes()
        {
            var types = from a in AppDomain.CurrentDomain.GetAssemblies()
                from t in a.GetTypes()
                let attribute = t.GetCustomAttribute(typeof(RuleTypeAttribute), true)
                where attribute != null
                select new { Type = t, Attribute = (RuleTypeAttribute) attribute };
        
            foreach(var v in types)
                RegisterRuleType(v.Attribute.type, v.Type);
        }
        
        public void ExportRuleTypes(string path)
        {
            string src = "";

            foreach(var k in _ruleTypes)
            {
                RuleTypeAttribute rta = k.Value.GetCustomAttribute<RuleTypeAttribute>();

                src += $"--{k.Key}{(rta == null ? "" : $" ({rta.description})")}\n";

                foreach(FieldInfo info in k.Value.GetFields())
                {
                    RuleFieldAttribute rfa = info.GetCustomAttribute<RuleFieldAttribute>();
                    DefaultValueAttribute dva = info.GetCustomAttribute<DefaultValueAttribute>();

                    if(rfa != null)
                    {
                        src += $"   {info.Name}{(rfa.required ? " (Required)" : "")}: {rfa.description}{(dva == null ? "" : $" (Default = {dva.Value})")}\n";
                    }
                }

                src += "\n";
            }

            File.WriteAllText(path, src);
        }
        #endregion

        #region Variables
        /// <summary>
        /// Define variable to be used by the rules
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void SetVariable(string key, string value)
        {
            _variables[key] = value;
        }

        /// <summary>
        /// Get defined variable
        /// </summary>
        /// <param name="key"></param>
        public string GetVariable(string key)
        {
            if (_variables.ContainsKey(key))
                return _variables[key];

            return null!;
        }
        #endregion

        #region Rules
        public void UpdateDynamicValues(Rule rule)
        {
            foreach (var dyn in rule.GetDynamicValues())
                _SetRuleValue(dyn.Key, PATTERN_VARS.Replace(dyn.Value, new MatchEvaluator(m => GetVariable(m.Groups[1].Value))), rule);
        }

        public void LoadJson(string json)
        {
            Var val = Var.FromFormattedString(json);

            Var variables = val["variables"];
            if (!variables.IsNull && variables.IsDictionary)
            {
                foreach (var v in variables.AsDictionary)
                {
                    SetVariable(v.Key, v.Value);
                }
            }

            Var rulesets = val["rulesets"];
            if (!rulesets.IsNull && rulesets.IsDictionary)
            {
                foreach (var v in rulesets.AsDictionary)
                {
                    RuleSet set = new RuleSet();

                    Var enabled = v.Value["enabled"];
                    if (!enabled.IsNull && enabled.IsBoolean)
                        if (!enabled.AsBoolean)
                            continue;

                    Var rules = v.Value["rules"];

                    if (!rules.IsNull && rules.IsList)
                    {
                        foreach (var r in rules.AsList)
                        {
                            if (r.IsString)
                            {
                                set.With(Parse(r.AsString));
                            }
                        }
                    }

                    Var children = v.Value["children"];

                    if (!children.IsNull && children.IsList)
                    {
                        foreach (var r in children.AsList)
                        {
                            if (r.IsString)
                            {
                                set.SetChildren(r.AsString);
                            }
                        }
                    }

                    With(v.Key, set);
                }
            }
        }

        /// <summary>
        /// Parse string to a rule
        /// </summary>
        /// <param name="rule"></param>
        /// <returns></returns>
        public Rule Parse(string rule)
        {
            string[] args = PATTERN_ARGS.Matches(rule).Cast<Match>().Where(m => m.Length > 0).Select(m => m.Value).ToArray();

            if (args.Length == 0) return null!;

            Type type = GetRuleType(args[0]);

            if (type == null) return null!;

            Rule r = ((Rule)Activator.CreateInstance(type)!)!;

            if (r == null) return null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("--"))
                {
                    arg = arg.Substring(2);

                    bool dynamic = false;
                    if (dynamic = arg.EndsWith("$"))
                        arg = arg.Substring(0, arg.Length - 1);

                    string value = "";
                    while (args.Length > i + 1 && !args[i + 1].StartsWith("--"))
                        value += $" {args[++i]}";

                    value = value.Length > 0 ? value.Substring(1) : "true";

                    if (value[0] == '\'')
                        value = value.Substring(1, value.Length - 2);

                    if (dynamic)
                        r.SetDynamicValue(arg, value);

                    //Replace variables
                    value = PATTERN_VARS.Replace(value, new MatchEvaluator(m => GetVariable(m.Groups[1].Value)));

                    //Apply value
                    _SetRuleValue(arg, value, r);
                }
            }

            return r;
        }

        private void _SetRuleValue(string field, string value, Rule rule)
        {
            FieldInfo info = rule.GetType().GetField(field)!;
            if (info != null)
            {
                Type ft = info.FieldType;

                if (ft == typeof(String) || ft == typeof(object))
                    info.SetValue(rule, value.Replace("/", "\\"));
                else if (ft == typeof(int))
                    info.SetValue(rule, int.Parse(value));
                else if (ft == typeof(float))
                    info.SetValue(rule, float.Parse(value));
                else if (ft == typeof(double))
                    info.SetValue(rule, double.Parse(value));
                else if (ft == typeof(bool))
                    info.SetValue(rule, bool.Parse(value));
            }
        }
        #endregion
    }

    public class RuleTest
    {
        private RuleEngine _engine;
        public RuleEngine engine => _engine;
        private Dictionary<string, List<Issue>> _issues = new Dictionary<string, List<Issue>>();

        private int _warningCount = 0;
        private int _problemCount = 0;
        public int issueCount => _warningCount + _problemCount;
        public int problemCount => _problemCount;
        public int warningCount => _warningCount;

        public RuleTest(RuleEngine engine)
        {
            _engine = engine;
        }

        public IEnumerable<Issue> GetIssues()
        {
            foreach (var l in _issues.Values)
                foreach (Issue i in l)
                    yield return i;
        }

        public void AddIssue(string key, Issue issue)
        {
            if (key == null) key = "";

            List<Issue> list;

            if (!_issues.ContainsKey(key))
            {
                list = new List<Issue>();
                _issues[key] = list;
            }
            else
                list = _issues[key];

            list.Add(issue);

            if (issue.type is Issue.Type.Warning)
                _warningCount++;
            else
                _problemCount++;
        }

        public void AddIssue(Issue issue)
        {
            AddIssue(null!, issue);
        }

        public override string ToString()
        {
            return $"Issues: {issueCount} (Warnings: {warningCount}, Problems: {problemCount})";
        }

        public void ExportToFile(string file)
        {
            String src = "";

            foreach (Issue issue in GetIssues())
            {
                src += issue.ToString() + "\n";
            }

            File.WriteAllText(file, src);
        }
    }

    public class Issue
    {
        public enum Type
        {
            Warning,
            Problem
        }

        private Type _type = Type.Warning;
        public Type type => _type;

        private String _message;
        public string message => _message;

        private String _path;
        public string path => _path;

        public Issue(Type type, string message, string path)
        {
            this._type = type;
            this._message = message;
            this._path = path;
        }

        public override string ToString() => $"[{path}]: {message}";
    }

    public class Rule
    {
        private bool _isDynamic = false;
        private Dictionary<string, string> _dynamicValues = null!;

        public void SetDynamicValue(string key, string value)
        {
            if (_dynamicValues == null)
            {
                _dynamicValues = new Dictionary<string, string>();
                _isDynamic = true;
            }

            _dynamicValues[key] = value;
        }

        public IEnumerable<KeyValuePair<string, string>> GetDynamicValues()
        {
            return _dynamicValues;
        }

        public void Call(string target, RuleTest test, object data)
        {
            if (target == null) return;

            foreach (string s in target.Split(" "))
                test.engine.GetRuleSet(s)?.Test(test, data);
        }

        public bool Test(RuleTest test, object data)
        {
            if (_isDynamic)
                test.engine.UpdateDynamicValues(this);

            return OnTest(test, data);
        }

        protected virtual bool OnTest(RuleTest test, object data)
        {
            return true;
        }
    }

    public class RuleSet
    {
        private string[] _children = new string[0];
        private List<Rule> _rules = new List<Rule>();

        public RuleSet With(Rule rule)
        {
            if (rule == null) return this;

            _rules.Add(rule);
            return this;
        }

        public RuleSet SetChildren(string children)
        {
            this._children = children.Split(" ");
            return this;
        }

        public void Test(RuleTest test, object data)
        {
            foreach (Rule rule in _rules)
                if (!rule.Test(test, data))
                    return;

            foreach (string s in _children)
                test.engine.GetRuleSet(s)?.Test(test, data);
        }
    }
    #endregion

    #region Attributes
    [AttributeUsage(AttributeTargets.Class)]
    public class RuleTypeAttribute : Attribute
    {
        public string type = null!;
        public string description = null!;

        public RuleTypeAttribute(string type, string description = null)
        {
            this.type = type;
            this.description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RuleFieldAttribute : Attribute
    {
        public bool required = false;
        public string description = null!;

        public RuleFieldAttribute(string description, bool required = false)
        {
            this.description = description;
            this.required = required;
        }
    }
    #endregion
}