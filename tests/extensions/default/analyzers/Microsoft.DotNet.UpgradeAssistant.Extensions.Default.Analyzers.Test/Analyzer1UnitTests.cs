using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

using VerifyCS = Analyzer1.Test.CSharpCodeFixVerifier<
    Microsoft.DotNet.UpgradeAssistant.Extensions.Default.CSharp.Analyzers.AllowHtmlAttributeAnalyzer,
    Microsoft.DotNet.UpgradeAssistant.Extensions.Default.CSharp.CodeFixes.AllowHtmlAttributeCodeFixer>;

namespace Analyzer1.Test
{
    public class Analyzer1UnitTest
    {
        //No diagnostics expected to show up
        [Fact]
        public async Task TestMethod1()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        //Diagnostic and CodeFix both triggered and checked for
        [Fact]
        public async Task TestMethod2()
        {
            var test = @"
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace TestProject.TestClasses
{
    public class UA0010
    {
        [{|#0:AllowHtml|}]
        [Required]
        public string Property1 { get; set; }

        [{|#1:AllowHtmlAttribute|}]
        [{|#2:MyNamespace.AllowHtml|}]
        public int Property2 { get; }

        [{|#3:Foo.AllowHtml|}, Required]
        public double Property3 { set { } }
    }
}";
            //using System;
            //using System.Collections.Generic;
            //using System.Linq;
            //using System.Text;
            //using System.Threading.Tasks;
            //using System.Diagnostics;

            //namespace ConsoleApplication1
            //{
            //    class {|#0:TypeName|}
            //    {
            //    }
            //}";

            var fixtest = @"
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace TestProject.TestClasses
{
    public class UA0010
    {
        [Required]
        public string Property1 { get; set; }

        [MyNamespace.AllowHtml]
        public int Property2 { get; }

        [Required]
        public double Property3 { set { } }
    }
}";

            var expected = new[]
            {
                VerifyCS.Diagnostic().WithLocation(0).WithArguments("AllowHtml"),
                VerifyCS.Diagnostic().WithLocation(1).WithArguments("AllowHtmlAttribute"),
                VerifyCS.Diagnostic().WithLocation(2).WithArguments("MyNamespace.AllowHtml"),
                VerifyCS.Diagnostic().WithLocation(3).WithArguments("Foo.AllowHtml"),
            };

            var compilerErrors = new[]
            {
                // /0/Test0.cs(3,18): error CS0234: The type or namespace name 'Mvc' does not exist in the namespace 'System.Web' (are you missing an assembly reference?)
                DiagnosticResult.CompilerError("CS0234").WithSpan(3, 18, 3, 21).WithArguments("Mvc", "System.Web"),

                // /0/Test0.cs(9,10): error CS0246: The type or namespace name 'AllowHtml' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(9, 10, 9, 19).WithArguments("AllowHtml"),

                // /0/Test0.cs(9,10): error CS0246: The type or namespace name 'AllowHtmlAttribute' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(9, 10, 9, 19).WithArguments("AllowHtmlAttribute"),

                // /0/Test0.cs(9,10): warning UA0010: Attribute 'AllowHtml' should be removed
                //VerifyCS.Diagnostic().WithSpan(9, 10, 9, 19).WithArguments("AllowHtml"),

                // /0/Test0.cs(13,10): error CS0246: The type or namespace name 'AllowHtmlAttribute' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(13, 10, 13, 28).WithArguments("AllowHtmlAttribute"),

                // /0/Test0.cs(13,10): error CS0246: The type or namespace name 'AllowHtmlAttributeAttribute' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(13, 10, 13, 28).WithArguments("AllowHtmlAttributeAttribute"),

                // /0/Test0.cs(13,10): warning UA0010: Attribute 'AllowHtmlAttribute' should be removed
                //VerifyCS.Diagnostic().WithSpan(13, 10, 13, 28).WithArguments("AllowHtmlAttribute"),

                // /0/Test0.cs(14,10): error CS0246: The type or namespace name 'MyNamespace' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(14, 10, 14, 21).WithArguments("MyNamespace"),

                // /0/Test0.cs(14,10): warning UA0010: Attribute 'MyNamespace.AllowHtml' should be removed
                //VerifyCS.Diagnostic().WithSpan(14, 10, 14, 31).WithArguments("MyNamespace.AllowHtml"),

                // /0/Test0.cs(17,10): error CS0246: The type or namespace name 'Foo' could not be found (are you missing a using directive or an assembly reference?)
                DiagnosticResult.CompilerError("CS0246").WithSpan(17, 10, 17, 13).WithArguments("Foo"),

                // /0/Test0.cs(17,10): warning UA0010: Attribute 'Foo.AllowHtml' should be removed
                //VerifyCS.Diagnostic().WithSpan(17, 10, 17, 23).WithArguments("Foo.AllowHtml"),
            };

            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }
    }
}
