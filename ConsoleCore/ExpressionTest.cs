using System;
using System.Linq.Expressions;
using System.Reflection;
using static ConsoleCore.Program;

namespace ShardingEFCoreTest
{
    public class sadsd
    {
        public class SD
        {
            public int Id { get; set; }
            public int Id2 { get; set; }
            public int Inad(int v)
            {
                return Id + 1 + v;
            }
        }
        public static Func<string, bool> ReBuildExpression(Expression<Func<string, bool>> lambd0, Expression<Func<string, bool>> lambd1)
        {
            ParameterExpression parameter = Expression.Parameter(typeof(string), "item");//parameter = {item} 
            Expression left = lambd0.Body;//lambd0.Body = {(item.Length > 2)}
                                          //   Expression right = lambd1.Body;//lambd1.Body = {(item.Length < 4)}

            Expression right = new RebindParameterVisitor(lambd1.Parameters[0], lambd0.Parameters[0]).Visit(lambd1.Body);
            BinaryExpression expression = Expression.AndAlso(left, right);//expression = {((item.Length > 2) AndAlso (item.Length < 4))} 
            Expression<Func<string, bool>> lambda = Expression.Lambda<Func<string, bool>>(expression, lambd0.Parameters[0]);//lambda = {item => ((item.Length > 2) AndAlso (item.Length < 4))}
            return lambda.Compile();//从作用域“”引用了“System.String”类型的变量“item”，但该变量未定义
        }
        public class RebindParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParameter;

            private readonly ParameterExpression _newParameter;

            public RebindParameterVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
            {
                _oldParameter = oldParameter;
                _newParameter = newParameter;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _oldParameter)
                {
                    return _newParameter;
                }

                return base.VisitParameter(node);
            }
        }
        public void dsf()
        {
            Expression<Func<SD, int>> e = e => e.Inad(e.Id2) + e.Id + 1;

            Expression<Func<SD, int>> e2 = e => e.Inad(e.Id2) + e.Id + 1;

            Expression right = new RebindParameterVisitor(e2.Parameters[0], e.Parameters[0]).Visit(e2.Body);
            var a = Expression.Lambda<Func<SD, int>>(Expression.And(e.Body, right), e.Parameters).Compile();
            var result = a.Invoke(new SD() { Id = 0, Id2 = 1 });
        }
    }
}
