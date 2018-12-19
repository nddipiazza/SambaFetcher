using System;
using System.IO;
using System.Security.AccessControl;
using System.Text;

namespace SmbFetcher {
  class Program {
    static void Main(string[] args) {
      string uncPath = args[0];
      string domain = args[1];
      string username = args[2];
      string action = args[3];
      string filePath = args[4];
      UNCAccessWithCredentials.UNCAccessWithCredentials unc = null;
      try {
        unc = new UNCAccessWithCredentials.UNCAccessWithCredentials();
        if (unc.NetUseWithCredentials(uncPath, username, domain, Environment.GetEnvironmentVariable("PWD"))) {
          if ("download".Equals(action)) {
            using (Stream myOutStream = Console.OpenStandardOutput()) {
              Stream inputStream = File.OpenRead(uncPath + filePath);
              byte[] buffer = new byte[1024];
              int bytesRead;
              while ((bytesRead = inputStream.Read(buffer, 0, 1024)) > 0) {
                myOutStream.Write(buffer, 0, bytesRead);
              }
              myOutStream.Flush();
              inputStream.Close();
            }
            Console.SetOut(System.IO.TextWriter.Null);
          } else if ("list".Equals(action)) {
            DirectoryInfo directoryInfo = new DirectoryInfo(uncPath + filePath);
            foreach (DirectoryInfo child in directoryInfo.GetDirectories()) {
              Console.WriteLine(child.FullName);
            }
            foreach (FileInfo child in directoryInfo.GetFiles()) {
              Console.WriteLine(child.FullName);
            }
          } else if ("metadata".Equals(action)) {
            FileInfo fileInfo = new FileInfo(uncPath + filePath);
            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            StringBuilder stringBuilder = new StringBuilder();
            foreach (FileSystemAccessRule fsar in fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))) {
              if (stringBuilder.Length != 0) {
                stringBuilder.Append(",");
              }
              stringBuilder.Append(fsar.IdentityReference.Value);
            }
            stringBuilder.Append("\t").Append(fileInfo.CreationTimeUtc).Append("\t").Append(fileInfo.LastAccessTimeUtc).Append("\t").Append(fileInfo.LastWriteTimeUtc).Append("\t").Append(fileInfo.Length);
            Console.WriteLine(stringBuilder);
          }
        } else {
          throw new Exception("Error - Couldn't log in");
        }
      } catch (Exception e) {
        Console.WriteLine("Error: {0}", e);
        Environment.Exit(1);
      } finally {
        unc.NetUseDelete();
        unc?.Dispose();
      }
    }
  }
}