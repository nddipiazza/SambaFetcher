using System;
using System.Threading;
using CommandLine;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;

namespace SmbFetcher {
  class CmdOptions {
    [Option('s', "share", Required = true, HelpText = "Name of the samba share.")]
    public string Share { get; set; }

    [Option('u', "username", Required = true, HelpText = "Username to authenticate with while accessing the windows share.")]
    public string Username { get; set; }

    [Option('d', "domain", HelpText = "Domain to authenticate with while accessing the windows share.")]
    public string Domain { get; set; }

    [Option('r', "port", Required = true, HelpText = "Port of the local web server.")]
    public int Port { get; set; }

    [Option('H', "smbHost", Required = true, HelpText = "Host of the samba server.")]
    public string SmbHost { get; set; }

    [Option('h', "host", Required = true, HelpText = "Host of the local web server.")]
    public string Host { get; set; }

    [Option('p', "password", HelpText = "Password of the user of whom we are authenticating.")]
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

      using (SmbServerModule module = new SmbServerModule(options.SmbHost, options.Domain, options.Username, options.Password, options.Share)) {
        var url = string.Format("http://{0}:{1}/", options.Host, options.Port);


        Console.WriteLine("Server URL: {0}", url);
        var server = new WebServer(url, RoutingStrategy.Regex);

        server.RegisterModule(module);

        var cts = new CancellationTokenSource();
        var task = server.RunAsync(cts.Token);

        task.Wait();  

      }
    }
  }
}