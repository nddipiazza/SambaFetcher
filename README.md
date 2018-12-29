# SambaFetcher

C# samba web server. Uses EmbedIO project to host a very simple web server capable of providing authenticated connection to Samba.

Usage: `SambaFetcher.exe`

Starts the samba web server. To stop it, kill the process.

```

  -P, --path        Required. UNC path of the Windows Share we are trying to access

  -u, --username    Required. Username to authenticate with while accessing the windows share

  -d, --domain      Required. Domain to authenticate with while accessing the windows share

  -r, --port        Required. Port of the local web server

  -h, --host        Required. Host of the local web server

  -p, --password    Password of the user of whom we are authenticating

  --help            Display this help screen.

  --version         Display version information.
```

# Web service

Endpoint: 

`http://{host}:{port}/?action={action}&path={unc path to file or folder}`

`path` = the path to a file or folder.
`action` = either `info` to get information about the file or folder, or `download` to download the file.
