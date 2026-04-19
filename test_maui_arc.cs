using System;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"C:\Program Files\dotnet\packs\Microsoft.Maui.Graphics\8.0.7\lib\net8.0\Microsoft.Maui.Graphics.dll");
        var type = asm.GetType("Microsoft.Maui.Graphics.ICanvas");
        var method = type.GetMethod("DrawArc");
        Console.WriteLine(method.ToString());
        foreach (var p in method.GetParameters()) {
            Console.WriteLine(p.Name + ": " + p.ParameterType);
        }
    }
}
