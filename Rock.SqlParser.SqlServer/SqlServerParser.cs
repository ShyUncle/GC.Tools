using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Irony.Parsing;
namespace Rock.SqlParser.SqlServer
{
    public class SqlServerParser
    {
        private static readonly LanguageData languageData = new LanguageData(new SqlServerGrammar());
        private static readonly Parser parser = new Parser(languageData);
        private readonly List<string> parsedFields = new List<string>();

        public void Execute(string command)
        {
            var tree = parser.Parse(command);
            if (tree.HasErrors())
            {
                return;
            }
            if (tree.Root.ChildNodes.Exists(x => x.Term.Name == "selectStmt"))
            {
                Console.WriteLine("确认是查询操作");
            }

            ParseTree(tree.Root);
        }

        private void ParseTree(ParseTreeNode node)
        {
            foreach (var child in node.ChildNodes)
            {
                ParseColumn(child);
                if (child.Term.Name == "idlist")
                {
                    ParsedTable = GetValue(child, "id_simple");
                }
                ParseTree(child);
            }
        }
        private void ParseColumn(ParseTreeNode node)
        {
            if (node.Term.Name != "columnItemList")
            {
                return;
            }
            foreach (var child in node.ChildNodes)
            {
                switch (child.Term.Name)
                {
                    case "columnItem":
                        parsedFields.Add(GetValue(child, "id_simple"));
                        break;
                }
            }
        }

        private string GetValue(ParseTreeNode node, string nodeName)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.Term.Name == nodeName)
                {
                    return (string)child.Token.Value;
                }
                else
                {
                    return GetValue(child, nodeName);
                }
            }
            return "";
        }
        public IEnumerable<string> ParsedFields => parsedFields;
        public string ParsedTable { get; private set; }
    }
}
