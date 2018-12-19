using System;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Runtime.InteropServices;
using BOOL = System.Boolean;
using DWORD = System.UInt32;
using LPWSTR = System.String;
using NET_API_STATUS = System.UInt32;

namespace SmbFetcher {
  public class UNCAccess {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USE_INFO_2 {
      internal LPWSTR ui2_local;
      internal LPWSTR ui2_remote;
      internal LPWSTR ui2_password;
      internal DWORD ui2_status;
      internal DWORD ui2_asg_type;
      internal DWORD ui2_refcount;
      internal DWORD ui2_usecount;
      internal LPWSTR ui2_username;
      internal LPWSTR ui2_domainname;
    }

    [DllImport("NetApi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern NET_API_STATUS NetUseAdd(
        LPWSTR UncServerName,
        DWORD Level,
        ref USE_INFO_2 Buf,
        out DWORD ParmError);

    [DllImport("NetApi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern NET_API_STATUS NetUseDel(
        LPWSTR UncServerName,
        LPWSTR UseName,
        DWORD ForceCond);

    private string sUNCPath;
    private string sUser;
    private string sPassword;
    private string sDomain;
    private int iLastError;
    public UNCAccess() {
    }
    public UNCAccess(string UNCPath, string User, string Domain, string Password) {
      login(UNCPath, User, Domain, Password);
    }
    public int LastError {
      get { return iLastError; }
    }

    /// <summary>
    /// Connects to a UNC share folder with credentials
    /// </summary>
    /// <param name="UNCPath">UNC share path</param>
    /// <param name="User">Username</param>
    /// <param name="Domain">Domain</param>
    /// <param name="Password">Password</param>
    /// <returns>True if login was successful</returns>
    public bool login(string UNCPath, string User, string Domain, string Password) {
      sUNCPath = UNCPath;
      sUser = User;
      sPassword = Password;
      sDomain = Domain;
      return NetUseWithCredentials();
    }
    private bool NetUseWithCredentials() {
      uint returncode;
      try {
        USE_INFO_2 useinfo = new USE_INFO_2();

        useinfo.ui2_remote = sUNCPath;
        useinfo.ui2_username = sUser;
        useinfo.ui2_domainname = sDomain;
        useinfo.ui2_password = sPassword;
        useinfo.ui2_asg_type = 0;
        useinfo.ui2_usecount = 1;
        uint paramErrorIndex;
        returncode = NetUseAdd(null, 2, ref useinfo, out paramErrorIndex);
        iLastError = (int)returncode;
        return returncode == 0;
      } catch {
        iLastError = Marshal.GetLastWin32Error();
        return false;
      }
    }

    /// <summary>
    /// Closes the UNC share
    /// </summary>
    /// <returns>True if closing was successful</returns>
    public bool NetUseDelete() {
      uint returncode;
      try {
        returncode = NetUseDel(null, sUNCPath, 2);
        iLastError = (int)returncode;
        return (returncode == 0);
      } catch {
        iLastError = Marshal.GetLastWin32Error();
        return false;
      }
    }
  }

  class Program {
    static void Main(string[] args) {
      string uncPath = args[0];
      string domain = args[1];
      string username = args[2];
      string action = args[3];
      string filePath = args.Length > 4 ? args[4] : "";
      UNCAccess unc = new UNCAccess(uncPath, username, domain, Environment.GetEnvironmentVariable("PWD"));
      try {
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
          Console.SetOut(TextWriter.Null);
        } else if ("info".Equals(action)) {
          Console.OutputEncoding = Encoding.UTF8;
          if (File.GetAttributes(uncPath + filePath).HasFlag(FileAttributes.Directory)) {
            Console.WriteLine("directory");
            DirectoryInfo directoryInfo = new DirectoryInfo(uncPath + filePath);
            foreach (DirectoryInfo child in directoryInfo.GetDirectories()) {
              Console.WriteLine("directory\t" + child.Name);
            }
            foreach (FileInfo child in directoryInfo.GetFiles()) {
              Console.Write("file\t" + child.Name);
              Console.Write("\t" + child.LastAccessTimeUtc);
              Console.Write("\t" + child.LastWriteTimeUtc);
              Console.WriteLine("\t" + child.CreationTimeUtc);
            }
          } else {
            Console.WriteLine("file");
            FileInfo fileInfo = new FileInfo(uncPath + filePath);
            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            StringBuilder stringBuilder = new StringBuilder();
            foreach (FileSystemAccessRule fsar in fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))) {
              if (stringBuilder.Length != 0) {
                stringBuilder.Append(",");
              }
              stringBuilder.Append(fsar.IdentityReference.Value);
            }
            Console.WriteLine(stringBuilder);
            Console.WriteLine(fileInfo.CreationTimeUtc);
            Console.WriteLine(fileInfo.LastAccessTimeUtc);
            Console.WriteLine(fileInfo.LastWriteTimeUtc);
            Console.WriteLine(fileInfo.Length);
          }
        }
      } catch (Exception e) {
        Console.WriteLine("Error: {0}", e);
        Environment.Exit(1);
      } finally {
        unc.NetUseDelete();
      }
    }
  }
}