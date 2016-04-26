﻿using CsScripts;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CsDebugScript
{
    /// <summary>
    /// Internal exception used for stopping interactive scripting
    /// </summary>
    internal class ExitRequestedException : Exception
    {
    }

    internal class InteractiveExecution : ScriptCompiler
    {
        /// <summary>
        /// The default prompt
        /// </summary>
        internal const string DefaultPrompt = "C#> ";

        /// <summary>
        /// The clear imports statement
        /// </summary>
        private const string ClearImportsStatement = "ClearImports;";

        /// <summary>
        /// The interactive script name. It is being used as file name during interactive commands compile time.
        /// </summary>
        internal const string InteractiveScriptName = "_interactive_script_.cs";

        /// <summary>
        /// The interactive script variables name. This must match variable name in InteractiveScriptBase class.
        /// </summary>
        private const string InteractiveScriptVariables = nameof(InteractiveScriptBase._Interactive_Script_Variables_);

        /// <summary>
        /// The interactive script output function called to write result if it is just a variable or expression
        /// </summary>
        private const string InteractiveScriptOutputFunction = nameof(InteractiveScriptBaseExtensions) + "." + nameof(InteractiveScriptBaseExtensions.Dump);

        /// <summary>
        /// The dynamic variables used in interactive script.
        /// </summary>
        private ExpandoObject dynamicVariables = new ExpandoObject();

        /// <summary>
        /// The interactive script base type
        /// </summary>
        private Type interactiveScriptBaseType = typeof(InteractiveScriptBase);

        /// <summary>
        /// The loaded scripts
        /// </summary>
        private List<string> loadedScripts = new List<string>();

        /// <summary>
        /// The imported code from the loaded scripts
        /// </summary>
        private string importedCode = "";

        /// <summary>
        /// The used references loaded from the imported code
        /// </summary>
        private string[] usedReferences = new string[0];

        /// <summary>
        /// The usings
        /// </summary>
        private List<string> usings = new List<string>(new string[] { "System", "System.Linq", "CsScripts" });

        /// <summary>
        /// Gets or sets the internal object writer. It is used for writing objects to host window.
        /// </summary>
        internal IObjectWriter InternalObjectWriter { get; set; } = new ConsoleObjectWriter();

        /// <summary>
        /// The script object writer used inside the interactive script.
        /// </summary>
        internal IObjectWriter ScriptObjectWriter = new DefaultObjectWriter();

        /// <summary>
        /// Runs interactive scripting mode.
        /// </summary>
        public void Run()
        {
            string prompt = DefaultPrompt;

            while (true)
            {
                // Read command
                string command = ReadCommand(prompt);

                // Check if we should execute C# command
                try
                {
                    if (!command.StartsWith("#dbg "))
                    {
                        Interpret(command, prompt);
                    }
                    else
                    {
                        Debugger.Execute(command.Substring(5));
                    }
                }
                catch (ExitRequestedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }

        /// <summary>
        /// Interprets C# code.
        /// </summary>
        /// <param name="code">The C# code.</param>
        /// <param name="prompt">The prompt.</param>
        public void Interpret(string code, string prompt = "")
        {
            try
            {
                UnsafeInterpret(code);
            }
            catch (CompileException ex)
            {
                CompileError[] errors = ex.Errors;

                if (!string.IsNullOrEmpty(prompt) && (errors[0].FileName.EndsWith(InteractiveScriptName) || errors.Count(e => !e.FileName.EndsWith(InteractiveScriptName)) == 0))
                {
                    CompileError e = errors[0];

                    Console.Error.WriteLine("{0}^ {1}", new string(' ', prompt.Length + e.Column - 1), e.FullMessage);
                }
                else
                {
                    Console.Error.WriteLine("Compile errors:");
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine(error.FullMessage);
                    }
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Interprets C# code, but without error handling.
        /// </summary>
        /// <param name="code">The C# code.</param>
        internal void UnsafeInterpret(string code)
        {
            if (code == ClearImportsStatement)
            {
                importedCode = "";
                loadedScripts = new List<string>();
                usedReferences = new string[0];
            }
            else
            {
                Execute(code);
            }
        }

        /// <summary>
        /// Reads the command from the user.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        private static string ReadCommand(string prompt)
        {
            // Write prompt
            prompt = prompt.Replace("%", "%%");
            Console.Write(prompt);

            // Read string
            return Context.Debugger.ReadInput();
        }

        /// <summary>
        /// Counts the number of occurrences in the specified string.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="find">The find.</param>
        private static int Count(string str, string find)
        {
            int index = str.IndexOf(find);
            int count = 0;

            while (index >= 0)
            {
                count++;
                index = str.IndexOf(find, index + find.Length);
            }

            return count;
        }

        /// <summary>
        /// Executes the specified code.
        /// </summary>
        /// <param name="code">The code.</param>
        private void Execute(string code)
        {
            // Extract imports and usings
            HashSet<string> newLoadedScripts = new HashSet<string>(loadedScripts);
            HashSet<string> newUsings = new HashSet<string>(usings);
            HashSet<string> imports = new HashSet<string>();
            HashSet<string> referencedAssemblies = new HashSet<string>(usedReferences);
            StringBuilder newImportedCode = new StringBuilder(importedCode);

            code = ImportCode(code, newUsings, imports);
            while (imports.Count > 0)
            {
                HashSet<string> newImports = new HashSet<string>();

                foreach (string import in imports)
                {
                    if (!newLoadedScripts.Contains(import))
                    {
                        string extension = Path.GetExtension(import).ToLower();

                        if (extension == ".dll" || extension == ".exe")
                        {
                            referencedAssemblies.Add(import);
                        }
                        else
                        {
                            string file = ImportFile(import, usings, newImports);

                            newImportedCode.AppendLine(file);
                            newLoadedScripts.Add(import);
                        }
                    }
                }

                imports = newImports;
            }

            // Compile code
            var compileResult = CompileByReplacingVariables(ref code, newUsings, newImportedCode.ToString(), referencedAssemblies.ToArray());

            // Report compile error
            // TODO: Remove injected code from error position
            List<CompileError> errors = new List<CompileError>();
            string[] codeLines = null;
            string varName = InteractiveScriptVariables + ".";

            foreach (var error in compileResult.Errors)
            {
                if (!error.IsWarning)
                {
                    if (error.FileName.EndsWith(InteractiveScriptName))
                    {
                        if (codeLines == null)
                        {
                            codeLines = code.Replace("\r\n", "\n").Split("\n".ToCharArray());
                        }

                        error.Column -= Count(codeLines[error.Line - 1], varName) * varName.Length;
                    }

                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
            {
                throw new CompileException(errors.ToArray());
            }

            // Execute compiled code
            var myClass = compileResult.CompiledAssembly.GetType(AutoGeneratedNamespace + "." + AutoGeneratedClassName);
            var method = myClass.GetMethod(AutoGeneratedScriptFunctionName);
            dynamic obj = Activator.CreateInstance(myClass);

            InteractiveScriptBase.Current = obj;
            obj._Interactive_Script_Variables_ = dynamicVariables;
            obj._InteractiveScriptBaseType_ = interactiveScriptBaseType;
            obj._InternalObjectWriter_ = InternalObjectWriter;
            obj.ObjectWriter = ScriptObjectWriter;
            method.Invoke(obj, new object[] { new string[] { } });
            interactiveScriptBaseType = obj._InteractiveScriptBaseType_;
            ScriptObjectWriter = obj.ObjectWriter;
            InteractiveScriptBase.Current = null;

            // Save imports, usings and references
            importedCode = newImportedCode.ToString();
            loadedScripts = newLoadedScripts.ToList();
            usedReferences = referencedAssemblies.ToArray();
            usings = newUsings.ToList();
        }

        private static string GetCodeName(Type type)
        {
            string typeString = type.FullName;

            if (typeString.Contains(".<") || typeString.Contains("+<"))
            {
                // TODO: Probably not the best one, but good enough for now
                return GetCodeName(type.GetInterfaces()[0]);
            }

            if (type.IsGenericType)
            {
                return string.Format("{0}<{1}>", typeString.Split('`')[0], string.Join(", ", type.GetGenericArguments().Select(x => GetCodeName(x))));
            }
            else
            {
                return typeString.Replace("+", ".");
            }
        }

        internal IEnumerable<string> GetScriptHelperCode(out string scriptStart, out string scriptEnd)
        {
            const string code = "<This is my unique code string>";
            string importedCode = FixImportedCode("");
            string generatedCode = GenerateCode(code, usings, importedCode);
            int codeStart = generatedCode.IndexOf(code), codeEnd = codeStart + code.Length;

            scriptStart = generatedCode.Substring(0, codeStart);
            scriptEnd = generatedCode.Substring(codeEnd);
            List<string> result = new List<string>();

            result.AddRange(DefaultAssemblyReferences);
            result.AddRange(usedReferences);
            return result;
        }

        private string FixImportedCode(string importedCode)
        {
            StringBuilder newImportedCode = new StringBuilder();

            newImportedCode.AppendLine(importedCode);
            foreach (var kvp in dynamicVariables)
            {
                string typeString = GetCodeName(kvp.Value.GetType());
                string name = kvp.Key;

                newImportedCode.Append(typeString);
                newImportedCode.Append(" ");
                newImportedCode.AppendLine(name);
                newImportedCode.AppendLine("{");
                newImportedCode.Append("get { return (");
                newImportedCode.Append(typeString);
                newImportedCode.Append(")(");
                newImportedCode.Append(InteractiveScriptVariables);
                newImportedCode.Append(".");
                newImportedCode.Append(name);
                newImportedCode.AppendLine("); }");
                newImportedCode.Append("set { ");
                newImportedCode.Append(InteractiveScriptVariables);
                newImportedCode.Append(".");
                newImportedCode.Append(name);
                newImportedCode.AppendLine(" = value; }");
                newImportedCode.AppendLine("}");
            }

            return newImportedCode.ToString();
        }

        private string GenerateCode(string code, IEnumerable<string> usings, string importedCode)
        {
            string generatedCode = "#line 1 \"" + InteractiveScriptName + "\"\n" + code + "\n#line default\n";

            return GenerateCode(usings, importedCode, generatedCode, interactiveScriptBaseType.FullName);
        }

        /// <summary>
        /// Compiles the code, but replaces any undeclared variable with dynamic one in InteractiveScriptBase.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="usings">The usings.</param>
        /// <param name="importedCode">The imported code.</param>
        /// <param name="referencedAssemblies">The referenced assemblies.</param>
        private CompileResult CompileByReplacingVariables(ref string code, IEnumerable<string> usings, string importedCode, params string[] referencedAssemblies)
        {
            // Add dynamic object fields as properties with types so one can use extensions
            importedCode = FixImportedCode(importedCode);

            while (true)
            {
                string generatedCode = GenerateCode(code, usings, importedCode);
                var compileResult = Compile(generatedCode, referencedAssemblies);
                bool fixedError = false;
                Dictionary<string, string> errorFixesByInsertion = new Dictionary<string, string>()
                {
                    // Fix for undeclared variable errors
                    { "CS0103", InteractiveScriptVariables + "." },
                    // Fix for expression as statement errors
                    { "CS0201", InteractiveScriptOutputFunction + "(" },
                    // Fix for missing closing bracket errors (we created them by inserting function call to Output function)
                    { "CS1026", ")" },
                };

                foreach (var error in compileResult.Errors)
                {
                    string errorFix;

                    // Try to fix errors by inserting "missing" code
                    if (error.FileName.EndsWith(InteractiveScriptName) && errorFixesByInsertion.TryGetValue(error.ErrorNumber, out errorFix))
                    {
                        code = FixErrorByInsertingString(code, error.Line, error.Column, errorFix);
                        fixedError = true;
                        break;
                    }
                    // Fix for not supplying string for field name in erase function (Roslyn compiler)
                    else if (error.FileName.EndsWith(InteractiveScriptName) && error.ErrorNumber == "CS0411" && error.FullMessage.Contains("'InteractiveScriptBase.erase<T1, T2>(T2)'"))
                    {
                        StringBuilder sb = new StringBuilder();

                        using (StringReader reader = new StringReader(code))
                        using (StringWriter writer = new StringWriter(sb))
                        {
                            for (int lineNumber = 1; ; lineNumber++)
                            {
                                string line = reader.ReadLine();

                                if (line == null)
                                    break;

                                if (lineNumber == error.Line)
                                {
                                    string before = line.Substring(0, error.Column - 1);
                                    string after = line.Substring(error.Column - 1);

                                    int eraseIndex = after.IndexOf("erase");
                                    int eraseEnd = eraseIndex + 5;
                                    int bracketIndex = after.IndexOf('(', eraseIndex) + 1;

                                    line = before + after.Substring(0, eraseIndex) + "erase_field" + after.Substring(eraseEnd, bracketIndex - eraseEnd) + "nameof(" + after.Substring(bracketIndex);
                                }

                                writer.WriteLine(line);
                            }
                        }

                        // Save the code and remove \r\n from the end
                        code = sb.ToString().Substring(0, sb.Length - 2);
                        fixedError = true;
                        break;
                    }
                    // Fix for not supplying string for field name in erase function (default compiler)
                    else if (error.FileName.EndsWith(InteractiveScriptName) && error.ErrorNumber == "CS0411" && error.FullMessage.Contains("'CsDebugScript.InteractiveScriptBase.erase<T1,T2>(T2)'"))
                    {
                        StringBuilder sb = new StringBuilder();

                        using (StringReader reader = new StringReader(code))
                        using (StringWriter writer = new StringWriter(sb))
                        {
                            for (int lineNumber = 1; ; lineNumber++)
                            {
                                string line = reader.ReadLine();

                                if (line == null)
                                    break;

                                if (lineNumber == error.Line)
                                {
                                    string before = line.Substring(0, error.Column - 1);
                                    string after = line.Substring(error.Column - 1);

                                    int eraseIndex = after.IndexOf("erase");
                                    int eraseEnd = eraseIndex + 5;
                                    int bracketIndex = after.IndexOf('(', eraseIndex) + 1;
                                    int closingBrackedIndex = after.IndexOf(')', bracketIndex);

                                    if (closingBrackedIndex > 0)
                                    {
                                        line = before + after.Substring(0, eraseIndex) + "erase_field" + after.Substring(eraseEnd, bracketIndex - eraseEnd) + "\"" + after.Substring(bracketIndex, closingBrackedIndex - bracketIndex) + "\"" + after.Substring(closingBrackedIndex);
                                        fixedError = true;
                                    }
                                }

                                writer.WriteLine(line);
                            }
                        }

                        // Save the code and remove \r\n from the end
                        code = sb.ToString().Substring(0, sb.Length - 2);
                        break;
                    }
                    // Try to fix missing ; at the end of the code
                    else if (error.FileName.EndsWith(InteractiveScriptName) && error.ErrorNumber == "CS1002")
                    {
                        bool incorrect = false;

                        using (StringReader reader = new StringReader(code))
                        {
                            for (int lineNumber = 1; !incorrect; lineNumber++)
                            {
                                string line = reader.ReadLine();

                                if (line == null)
                                    break;

                                if (lineNumber == error.Line)
                                {
                                    if (line.Substring(error.Column - 1).Trim().Length != 0)
                                    {
                                        incorrect = true;
                                    }
                                    else if (reader.ReadToEnd().Trim().Length != 0)
                                    {
                                        incorrect = true;
                                    }
                                }
                            }
                        }

                        if (!incorrect)
                        {
                            code += ";";
                            fixedError = true;
                            break;
                        }
                    }
                }

                if (!fixedError)
                {
                    return compileResult;
                }
            }
        }

        /// <summary>
        /// Fixes the error by inserting string and error position.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="errorLineNumber">The error line number.</param>
        /// <param name="errorColumn">The error column.</param>
        /// <param name="fix">The fixing string.</param>
        /// <returns>Fixed code</returns>
        private static string FixErrorByInsertingString(string code, int errorLineNumber, int errorColumn, string fix)
        {
            StringBuilder sb = new StringBuilder();

            using (StringReader reader = new StringReader(code))
            using (StringWriter writer = new StringWriter(sb))
            {
                for (int lineNumber = 1; ; lineNumber++)
                {
                    string line = reader.ReadLine();

                    if (line == null)
                        break;

                    if (lineNumber == errorLineNumber)
                    {
                        line = line.Substring(0, errorColumn - 1) + fix + line.Substring(errorColumn - 1);
                    }

                    writer.WriteLine(line);
                }
            }

            // Save the code and remove \r\n from the end
            return sb.ToString().Substring(0, sb.Length - 2);
        }
    }
}
