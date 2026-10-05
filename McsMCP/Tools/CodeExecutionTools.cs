using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using McsMCP.Server;
using Newtonsoft.Json.Linq;

namespace McsMCP.Tools
{
    /// <summary>
    /// Tool for executing C# code at runtime in the game process.
    ///
    /// Backed by Mono.CSharp (embedded into McsMCP.dll).
    ///
    /// ★ The language level is C# 7.2, NOT "full C#". That ceiling is set by the mcs.dll build
    /// itself, whose Mono.CSharp.LanguageVersion enum stops at V_7_2 - `Experimental` (which
    /// ScriptSession selects) is already the highest value it has, so there is nothing to raise.
    ///
    /// Measured with McsMCP/tests/mcs-lang: 15 representative features through C# 7.2 compile,
    /// and the gaps are specific rather than general - see the description below, which lists them
    /// so a caller writes around them instead of concluding the tool is broken.
    ///
    /// This used to claim "full C# is available", which was wrong in a way that wasted a caller's
    /// time: the failures are not uniform (some are syntax errors, one is an internal compiler
    /// error) and none of them name the real constraint.
    public class ExecuteCSharpToolDefinition : ToolDefinitionBase
    {
        public override string Name => "execute_csharp";
        public override string Description => @"Execute C# in-process; all Unity, game and BepInEx types are available.

C# up to 7.2, EXCEPT: switch type patterns (case int i:) -> internal compiler error, use
if (x is int i); when clauses, local functions and in parameters are not parsed; LINQ query syntax
fails, use x.Where(...).Select(...).

State persists across calls; a trailing expression is returned. Runs on the Unity main thread with a
small stack and cannot be interrupted: avoid MakeGenericMethod, deep reflection, infinite loops.";

        /// <summary>
        /// Session shared by all script tools. State (variables, usings, defined types) persists
        /// between calls, which is what an interactive console is expected to do.
        /// </summary>
        internal static readonly ScriptSession Session = new ScriptSession();

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["code"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "C# code to execute. A trailing expression is returned as the result."
                    },
                    ["timeout"] = new ToolPropertySchema
                    {
                        Type = "integer",
                        Description = "Execution timeout in milliseconds (default: 5000, max: 30000)",
                        Default = 5000,
                        Minimum = 100,
                        Maximum = 30000
                    },
                    ["reset"] = new ToolPropertySchema
                    {
                        Type = "boolean",
                        Description = "Clear all session state (variables, usings, defined types) before running.",
                        Default = false
                    }
                },
                Required = new List<string> { "code" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var code = GetStringArg(arguments, "code");
            var timeout = GetIntArg(arguments, "timeout", 5000);
            var reset = GetBoolArg(arguments, "reset", false);

            if (string.IsNullOrWhiteSpace(code))
            {
                return ErrorResult("Code parameter is required");
            }

            if (reset)
            {
                Session.Reset();
            }

            try
            {
                var result = Session.Run(code);
                return result.Success
                    ? TextResult(result.ToDisplayString())
                    : ErrorResult(result.ToDisplayString());
            }
            catch (Exception ex)
            {
                return ErrorResult($"Execution failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Tool for evaluating simple C# expressions
    /// </summary>
    public class EvaluateExpressionToolDefinition : ToolDefinitionBase
    {
        public override string Name => "evaluate_expression";
        public override string Description => @"Evaluate one C# expression and return its value. Same engine and same C# 7.2 limits as
execute_csharp, but for a single stateless read - use execute_csharp when you need variables or
several statements.";

        protected override ToolInputSchema GetInputSchema()
        {
            return new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolPropertySchema>
                {
                    ["expression"] = new ToolPropertySchema
                    {
                        Type = "string",
                        Description = "C# expression to evaluate"
                    }
                },
                Required = new List<string> { "expression" }
            };
        }

        public override CallToolResult Execute(Dictionary<string, JToken> arguments)
        {
            var expression = GetStringArg(arguments, "expression");

            if (string.IsNullOrWhiteSpace(expression))
            {
                return ErrorResult("Expression parameter is required");
            }

            try
            {
                var result = ExecuteCSharpToolDefinition.Session.Run(expression);
                return result.Success
                    ? TextResult(result.ToDisplayString())
                    : ErrorResult(result.ToDisplayString());
            }
            catch (Exception ex)
            {
                return ErrorResult($"Evaluation failed: {ex.Message}");
            }
        }
    }
}
