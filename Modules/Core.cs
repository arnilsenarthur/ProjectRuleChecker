using System.ComponentModel;
using System.Text.RegularExpressions;

namespace RuleChecker.Modules.Core
{
    [RuleType("match", "Stops a ruleset execution if the value does not match the pattern")]
    public class MatchRule : Rule
    {
        [RuleField("Defines the alue to be checked", true)]
        public string value = null!;
        [RuleField("Defines the Regex pattern", true)]
        public string pattern = null!;
        [RuleField("Defines if the result will be inverted")]
        [DefaultValue(false)]
        public bool inverse = false;

        protected override bool OnTest(RuleTest test, object data)
        {
            if(value == null || pattern == null) return false;

            bool m = Regex.IsMatch(value, pattern);
            return inverse ? !m : m;    
        }
    }

    [RuleType("print", "Print a message to console")]
    public class PrintRule : Rule
    {
        [RuleField("Defines the message to be printed")]
        public string message = null!;

        protected override bool OnTest(RuleTest test, object data)
        {
            Console.WriteLine(this.message);
            return true;
        }
    }

    public abstract class BaseIssueRule : Rule
    {
        [RuleField("Defines the issue sorting key", true)]
        public string key = null!;

        [RuleField("Defines the message to be shown", true)]
        public string message = null!;

        [RuleField("Defines the navigation path", true)]
        public string path = null;

        public Issue.Type type;

        public BaseIssueRule(Issue.Type type)
        {
            this.type = type;
        }

        protected override bool OnTest(RuleTest test, object data)
        {
            if(message == null || path == null) return true;

            test.AddIssue(key, new Issue(type, message, path));

            return true;
        }
    }

    [RuleType("warning", "Marks a warning issue")]
    public class WarningIssueRule : BaseIssueRule
    {
        public WarningIssueRule() : base(Issue.Type.Warning) {}
    }

    [RuleType("problem", "Marks a problem issue")]
    public class ProblemIssueRule : BaseIssueRule
    {
        public ProblemIssueRule() : base(Issue.Type.Problem) {}
    }
}