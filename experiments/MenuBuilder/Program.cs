using System;
using ACadSmartLeader;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Usage: MenuBuilder.exe <output.cuix>");
        MenuDefinition.Write(args[0]);
        Console.WriteLine("Partial CUIX created: " + args[0]);
    }
}
