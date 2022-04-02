using Irony.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rock.SqlParser.Irony
{
    internal class SqlServerParser
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

            ParseTree(tree.Root);
        }

        private void ParseTree(ParseTreeNode node)
        {
            foreach (var child in node.ChildNodes)
            {
                switch (child.Term.Name)
                {
                    case "stmtList":
                        break;
                    case "Id":
                        foreach (var child1 in child.ChildNodes)
                        {
                            parsedFields.Add(child1.Token.Text);
                        }
                        break;
                    case "TABLE":
                        ParsedTable = node.ChildNodes[0].Token.Text;
                        break;

                }
                ParseTree(child);
            }
        }

        public IEnumerable<string> ParsedFields => parsedFields;
        public string ParsedTable { get; private set; }
    }
}
