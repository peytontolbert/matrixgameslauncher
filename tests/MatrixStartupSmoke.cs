using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

// Explicit local development smoke test, never run by the normal test suite.
// Uses the production launcher handoff with an unissued loopback-only ticket.
// This can verify game bootstrap, not a server join or browser/native crossplay.
class MatrixStartupSmoke {
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Length!=3)throw new ArgumentException("Expected runtime directory, owned GTA installation, and scratch profile directory");
            var server=new ServerEntry{Id="matrix-local-startup",Game="gta-v",Name="Local startup check"};
            var runtime=MatrixNativeRuntime.Inspect(Path.GetFullPath(args[0]));
            var grant=new Dictionary<string,object>{{"schema",4},{"runtime",MatrixNativeRuntime.Id},{"game","gta-v"},{"server",server.Id},
                {"endpoint","https://127.0.0.1:4596/"},{"gameBuild",3889},{"ticket",new string('a',43)},
                {"expiresAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+300000}};
            var location=new GameLocations{InstallDirectory=Path.GetFullPath(args[1]),DocumentsDirectory=Path.GetFullPath(args[2])};
            new GtaV().ValidateLocations(location);
            Console.WriteLine("Matrix launcher bootstrap check only; no server join is expected.");
            using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(4)))
                runtime.Launch(grant,server,location,Console.WriteLine,cancel.Token).GetAwaiter().GetResult();
            return 0;
        } catch(Exception error) {Console.Error.WriteLine(error.Message);return 1;}
    }
}
