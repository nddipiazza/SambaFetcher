using System;
using System.Threading;
using CommandLine;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;

namespace SmbFetcher {
  internal class CmdOptions {
    [Option('P', "path", Required = true, HelpText = "UNC path of the Windows Share we are trying to access")]
    public string UncPath { get; set; }

    [Option('u', "username", Required = true, HelpText = "Username to authenticate with while accessing the windows share")]
    public string Username { get; set; }

    [Option('d', "domain", Required = true, HelpText = "Domain to authenticate with while accessing the windows share")]
    public string Domain { get; set; }

    [Option('r', "port", Required = true, HelpText = "Port of the local web server")]
    public int Port { get; set; }

    [Option('p', "password", HelpText = "Password of the user of whom we are authenticating")]
    public string Password { get; set; }
  }
  class Program {

    static void Main(string[] args) {
      var cmdOptions = Parser.Default.ParseArguments<CmdOptions>(args);
      cmdOptions.WithParsed(
          options => {
            Run(options);
          });
    }

    public static void Run(CmdOptions options) {
      Console.WriteLine("Path: {0}", options.UncPath);
      Console.WriteLine("Username: {0}", options.Username);

      UNCAccess unc = new UNCAccess(options.UncPath, options.Username, options.Domain, Environment.GetEnvironmentVariable("PWD"));

      try {
        var url = string.Format("http://localhost:{0}/", options.Port);

        var server = new WebServer(url, RoutingStrategy.Regex);

        server.RegisterModule(new SmbServerModule());

        var cts = new CancellationTokenSource();
        var task = server.RunAsync(cts.Token);

        task.Wait();  
      } finally {
        unc?.NetUseDelete();
      }

    }

  }
}