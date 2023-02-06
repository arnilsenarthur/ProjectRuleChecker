using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleChecker.Modules.CSharp
{
    public class CSharpItem
    {
        public SyntaxNode node => _node;
        private SyntaxNode _node = null;

        public string position => CSharpUtils.GetPosition(node);

        #region Data
        public string dataName => LoadName == null ? null : LoadName();
        public string dataRegion => _dataRegion;
        private string _dataRegion = "??";
        public CSharpItem dataType => LoadType == null ? null : LoadType();
        public CSharpItem dataNamespace => LoadNamespace == null ? null : LoadNamespace();
        public bool dataIsConstant => LoadIsConstant == null ? false : LoadIsConstant();
        public bool dataIsPrivate => LoadIsPrivate == null ? false : LoadIsPrivate();
        public bool dataIsStatic => LoadIsStatic == null ? false : LoadIsStatic();
        public bool dataIsPublic => LoadIsPublic == null ? false : LoadIsPublic();
        public bool dataIsProtected => LoadIsProtected == null ? false : LoadIsProtected();

        public Func<string> LoadName;
        public Func<CSharpItem> LoadNamespace;
        public Func<CSharpItem> LoadType;

        public Func<bool> LoadIsStatic;
        public Func<bool> LoadIsConstant;

        public Func<bool> LoadIsPrivate;
        public Func<bool> LoadIsPublic;
        public Func<bool> LoadIsProtected;
        #endregion

        public CSharpItem(SyntaxNode node, string region)
        {
            this._node = node;
            this._dataRegion = region;
        }

    }

    public class CSharpFile
    {
        public string path => _path;
        private string _path = null;

        public SyntaxNode root => _root;
        private SyntaxNode _root = null;

        public CSharpFile(string path, SyntaxNode root)
        {
            this._path = path;
            this._root = root;
        }
    }

    [RuleType("csharp-open", "Parse a C# file for code inspection")]
    public class CSharpOpenRule : Rule
    {
        [RuleField("Defines the path of the csharp file to be open")]
        public string path = null;

        [RuleField("Defines the rulesets that will be run with the cs file open", true)]
        public string then = null;

        protected override bool OnTest(RuleTest test, object data)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(path));
            SyntaxNode root = tree.GetRoot();

            CSharpFile file = new CSharpFile(path, root);

            Call(then, test, file);

            return true;
        }
    }

    [RuleType("csharp-filter")]
    public class CSharpFilter : Rule
    {
        public class Region
        {
            public string name;
            public int from;
            public int to;

            public override string ToString() => $"{name} ({from}:{to})";

            public bool isInside(int pos)
            {
                return pos >= from && pos <= to;
            }
        }

        public string type = null;
        public string name = null;
        public string namespc = null;
        public string typeowner = null;
        public string region = null;

        public bool isstatic = false;

        public bool isconst = false;
        public bool isprivate = false;
        public bool ispublic = false;
        public bool isprotected = false;

        public string then = null;

        protected override bool OnTest(RuleTest test, object data)
        {
            CSharpFile file = data as CSharpFile;
            SyntaxNode root = file.root;

            List<Region> regions = new List<Region>();
            Stack<Region> curRegions = new Stack<Region>();

            foreach (SyntaxTrivia node in root.DescendantTrivia())
            {
                if (node.Kind() == SyntaxKind.RegionDirectiveTrivia)
                {
                    string region = node.ToString().Split(" ", 2)[1];
                    curRegions.Push(new Region { name = region, from = node.Span.Start });
                }

                if (node.Kind() == SyntaxKind.EndRegionDirectiveTrivia)
                {
                    Region region = curRegions.Pop();
                    region.to = node.Span.End;
                    regions.Add(region);
                }
            }

            foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
            {
                string type = node.GetType().Name.Replace("DeclarationSyntax", "").Replace("DirectiveTriviaSyntax", "").ToLower();

                if (this.type != null && !Regex.IsMatch(type, this.type)) continue;

                string region = string.Join("/", regions.Where((r) => r.isInside(node.Span.Start)).OrderBy((r) => r.from).Select((r) => r.name).ToArray());
                CSharpItem item = CSharpUtils.GetCSharpItem(node, region);

                #region Filters
                if (this.name != null && !Regex.IsMatch(item.dataName, this.name)) continue;
                if (this.namespc != null && !Regex.IsMatch(item.dataNamespace.dataName, this.namespc)) continue;
                if (this.typeowner != null && !Regex.IsMatch(item.dataType.dataName, this.typeowner)) continue;
                if (this.region != null && !Regex.IsMatch(item.dataRegion, this.region)) continue;

                if (this.isconst != item.dataIsConstant) continue;
                if (this.isstatic != item.dataIsStatic) continue;
                if (this.isprivate && !item.dataIsPrivate) continue;
                if (this.ispublic && !item.dataIsPublic) continue;
                if (this.isprotected && !item.dataIsProtected) continue;
                #endregion

                #region Variables
                test.engine.SetVariable("csharp-name", item.dataName);
                test.engine.SetVariable("csharp-type", item.dataType == null ? null : item.dataType.dataName);
                test.engine.SetVariable("csharp-namespace", item.dataNamespace == null ? null : item.dataNamespace.dataName);
                test.engine.SetVariable("csharp-region", item.dataRegion);
                test.engine.SetVariable("csharp-position", item.position);
                #endregion

                Call(then, test, item);
            }

            return true;
        }
    }

    public static class CSharpUtils
    {
        public static bool IsPrivateModifier(SyntaxToken modifier)
        {
            return modifier.Kind() == SyntaxKind.PrivateKeyword;
        }

        public static bool IsStaticModifier(SyntaxToken modifier)
        {
            return modifier.Kind() == SyntaxKind.StaticKeyword;
        }

        public static bool IsProtectedModifier(SyntaxToken modifier)
        {
            return modifier.Kind() == SyntaxKind.StaticKeyword;
        }

        public static bool IsPublicModifier(SyntaxToken modifier)
        {
            return modifier.Kind() == SyntaxKind.PublicKeyword;
        }

        public static bool IsConstantModifier(SyntaxToken modifier)
        {
            return modifier.Kind() == SyntaxKind.ConstKeyword;
        }

        public static CSharpItem GetCSharpItem(SyntaxNode node, string region)
        {
            CSharpItem item = new CSharpItem(node, region);

            item.LoadNamespace = () => GetCSharpItem(GetParentSyntax<NamespaceDeclarationSyntax>(node), "");
            item.LoadType = () => GetCSharpItem(GetParentSyntax<TypeDeclarationSyntax>(node), "");

            if (node is MemberDeclarationSyntax)
            {
                MemberDeclarationSyntax member = node as MemberDeclarationSyntax;

                item.LoadIsPrivate = () => member.Modifiers.Any(IsPrivateModifier);
                item.LoadIsPublic = () => member.Modifiers.Any(IsPublicModifier);
                item.LoadIsProtected = () => member.Modifiers.Any(IsProtectedModifier);

                item.LoadIsStatic = () => member.Modifiers.Any(IsStaticModifier);
                item.LoadIsConstant = () => member.Modifiers.Any(IsConstantModifier);
            }

            if (node is TypeDeclarationSyntax)
            {
                item.LoadName = (node as TypeDeclarationSyntax).Identifier.ToString;
            }
            else if (node is MethodDeclarationSyntax)
            {
                item.LoadName = (node as MethodDeclarationSyntax).Identifier.ToString;
            }
            else if (node is PropertyDeclarationSyntax)
            {
                item.LoadName = (node as PropertyDeclarationSyntax).Identifier.ToString;
            }
            else if (node is NamespaceDeclarationSyntax)
            {
                item.LoadName = (node as NamespaceDeclarationSyntax).Name.ToString;
            }
            else if (node is FieldDeclarationSyntax)
            {
                item.LoadName = () =>
                {
                    foreach (SyntaxNode n in node.DescendantNodes())
                        if (n is VariableDeclaratorSyntax)
                        {
                            string name = (n as VariableDeclaratorSyntax).ToString();
                            int idx = name.IndexOf(" ");
                            if (idx > 0) name = name.Substring(0, idx);

                            return name;
                        }

                    return null;
                };
            }

            return item;
        }

        public static T GetParentSyntax<T>(SyntaxNode syntaxNode) where T : SyntaxNode
        {
            if (syntaxNode == null)
                return null;

            syntaxNode = syntaxNode.Parent;

            if (syntaxNode == null)
                return null;

            if (syntaxNode.GetType() == typeof(T) || syntaxNode.GetType().IsSubclassOf(typeof(T)))
                return syntaxNode as T;

            return GetParentSyntax<T>(syntaxNode);
        }

        public static string GetPosition(SyntaxNode node)
        {
            FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);
            return span.StartLinePosition.ToString();
        }
    }
}