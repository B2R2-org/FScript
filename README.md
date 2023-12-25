FScript
===

![image](assets/fscript.png)

FScript (Fsharp Script) provides a convenient way to write scripts in F# as if
you are writing a shell script. It is a cross-platform tool that can run on
Windows, Linux, and macOS as long as you have .NET SDK 7.0 or above installed.

# Basic Usage

FScript is a library that can be imported into your F# script file. All you need
to do is to add the following line to the top of your script file:

```fsharp
#r "nuget: B2R2.FScript"
```

You can then write your script as if you are writing a shell script. For
example, the following script will execute the `ls -la` command and print the
output to the console:

```fsharp
#!/usr/bin/env -S dotnet fsi
#r "nuget: B2R2.FScript"

open B2R2.FScript

!"ls" ["-la"]
=| fun cmd ->
  cmd.ReadStdout ()
  |> printfn "%s"
```

Note that the bang operator `!` is used to execute a command, and the command
instance is passed with the `=|` operator so that you can print out the output.
You can also pipe the output to another command using the `=>` operator.

```fsharp
#!/usr/bin/env -S dotnet fsi
#r "nuget: B2R2.FScript"

open B2R2.FScript
open System

!"cat" ["README.md"]
=> !"tail" ["-n"; "1"]
=| fun cmd ->
  Console.WriteLine $"Exit: {cmd.ExitCode.Value}"
  Console.WriteLine $"Stdout:\n{cmd.ReadStdout ()}"
  Console.WriteLine $"Stderr:\n{cmd.ReadStderr ()}"
```

The above script will print out the last line of this `README.md` file. You can
use the `=>` operator to pipe the output of `cat` to `tail` and print out the
output of `tail` to the console. Note also that you can get the exit code of the
last command by accessing the `ExitCode` property of the command instance.

# TaskQueue

FScript provides a way to run multiple commands in parallel. To do so, you first
instantiates a `TaskQueue` instance and then add commands to the queue. It will
automatically consume the commands and run them in parallel by maximizing the
number of cores on your machine.