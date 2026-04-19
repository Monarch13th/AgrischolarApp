using System;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"C:\Program Files\dotnet\packs\Microsoft.Maui.Graphics\8.0.7\lib\net8.0\Microsoft.Maui.Graphics.dll");
        var type = asm.GetType("Microsoft.Maui.Graphics.PathF");
        var method = type.GetMethod("AddArc", new Type[] { typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool) });
        if (method != null) {
            Console.WriteLine(method.ToString());
            foreach (var p in method.GetParameters()) {
                Console.WriteLine(p.Name + ": " + p.ParameterType);
            }
        } else {
            Console.WriteLine("Method not found");
        }
    }
}
