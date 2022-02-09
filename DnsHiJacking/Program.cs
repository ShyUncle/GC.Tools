using DnsHiJacking;
using System.IO;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
try
{
    var cancelToken = new CancellationTokenSource().Token;
    new DnsInterceptor().InterceptAsync(cancelToken);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}

Console.ReadLine();

