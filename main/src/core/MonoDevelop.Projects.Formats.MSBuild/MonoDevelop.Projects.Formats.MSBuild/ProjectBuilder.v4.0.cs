// 
// ProjectBuilder.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Logging;
using Microsoft.Build.Execution;
using System.Xml;

namespace MonoDevelop.Projects.Formats.MSBuild
{
	public class ProjectBuilder: MarshalByRefObject, IProjectBuilder
	{
		readonly ProjectCollection engine;
		readonly string file;
		ILogWriter currentLogWriter;
		readonly ConsoleLogger consoleLogger;
		readonly BuildEngine buildEngine;

		public ProjectBuilder (BuildEngine buildEngine, ProjectCollection engine, string file)
		{
			this.file = file;
			this.engine = engine;
			this.buildEngine = buildEngine;
			consoleLogger = new ConsoleLogger (LoggerVerbosity.Normal, LogWriteLine, null, null);
			Refresh ();
		}
		
		public void Dispose ()
		{
			buildEngine.UnloadProject (file);
		}

		public void Refresh ()
		{
			buildEngine.UnloadProject (file);
		}

		public void RefreshWithContent (string projectContent)
		{
			buildEngine.UnloadProject (file);
			buildEngine.SetUnsavedProjectContent (file, projectContent);
		}
		
		void LogWriteLine (string txt)
		{
			if (currentLogWriter != null)
				currentLogWriter.WriteLine (txt);
		}
		
		public MSBuildResult[] RunTarget (string target, ProjectConfigurationInfo[] configurations, ILogWriter logWriter,
			MSBuildVerbosity verbosity)
		{
			MSBuildResult[] result = null;
			BuildEngine.RunSTA (delegate
			{
				try {
					var project = SetupProject (configurations);
					currentLogWriter = logWriter;

					var logger = new LocalLogger (file);
					engine.UnregisterAllLoggers ();
					engine.RegisterLogger (logger);
					engine.RegisterLogger (consoleLogger);

					consoleLogger.Verbosity = GetVerbosity (verbosity);
					
					project.Build (target);
					
					result = logger.BuildResult.ToArray ();
				} catch (Microsoft.Build.Exceptions.InvalidProjectFileException ex) {
						var r = new MSBuildResult (
							file, false, ex.ErrorSubcategory, ex.ErrorCode, ex.ProjectFile,
							ex.LineNumber, ex.ColumnNumber, ex.EndLineNumber, ex.EndColumnNumber,
							ex.BaseMessage, ex.HelpKeyword);
						logWriter.WriteLine (r.ToString ());
						result = new [] { r };
				} finally {
					currentLogWriter = null;
				}
			});
			return result;
		}
		
		LoggerVerbosity GetVerbosity (MSBuildVerbosity verbosity)
		{
			switch (verbosity) {
			case MSBuildVerbosity.Quiet:
				return LoggerVerbosity.Quiet;
			case MSBuildVerbosity.Minimal:
				return LoggerVerbosity.Minimal;
			case MSBuildVerbosity.Normal:
			default:
				return LoggerVerbosity.Normal;
			case MSBuildVerbosity.Detailed:
				return LoggerVerbosity.Detailed;
			case MSBuildVerbosity.Diagnostic:
				return LoggerVerbosity.Diagnostic;
			}
		}

		public string[] GetAssemblyReferences (ProjectConfigurationInfo[] configurations)
		{
			string[] refsArray = null;

			BuildEngine.RunSTA (delegate
			{
				var project = SetupProject (configurations);
				
				// We are using this BuildProject overload and the BuildSettings.None argument as a workaround to
				// an xbuild bug which causes references to not be resolved after the project has been built once.
				var pi = project.CreateProjectInstance ();
				pi.Build ("ResolveAssemblyReferences", null);
				var refs = new List<string> ();
				foreach (ProjectItemInstance item in pi.GetItems ("ReferencePath"))
					refs.Add (UnescapeString (item.EvaluatedInclude));
				refsArray = refs.ToArray ();
			});
			return refsArray;
		}
		
		Project SetupProject (ProjectConfigurationInfo[] configurations)
		{
			Project project = null;

			foreach (var pc in configurations) {
				var p = ConfigureProject (pc.ProjectFile, pc.Configuration, pc.Platform);
				if (pc.ProjectFile == file)
					project = p;
			}

			Environment.CurrentDirectory = Path.GetDirectoryName (file);
			return project;
		}

		Project ConfigureProject (string file, string configuration, string platform)
		{			
			var p = engine.GetLoadedProjects (file).FirstOrDefault ();
			if (p == null) {
				var content = buildEngine.GetUnsavedProjectContent (file);
				if (content == null)
					p = engine.LoadProject (file);
				else {
					p = engine.LoadProject (new XmlTextReader (new StringReader (content)));
					p.FullPath = file;
				}
			}
			p.SetProperty ("Configuration", configuration);
			if (!string.IsNullOrEmpty (platform))
				p.SetProperty ("Platform", platform);
			else
				p.SetProperty ("Platform", "");
			
			return p;
		}

		public override object InitializeLifetimeService ()
		{
			return null;
		}

		//from MSBuildProjectService
		static string UnescapeString (string str)
		{
			int i = str.IndexOf ('%');
			while (i != -1 && i < str.Length - 2) {
				int c;
				if (int.TryParse (str.Substring (i+1, 2), System.Globalization.NumberStyles.HexNumber, null, out c))
					str = str.Substring (0, i) + (char) c + str.Substring (i + 3);
				i = str.IndexOf ('%', i + 1);
			}
			return str;
		}
	}
}
