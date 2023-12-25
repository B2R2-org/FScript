open B2R2.FScript
open System

!"cat" ["README.md"]
=> !"sort" []
=> !"tail" ["-n"; "1"]
=| fun cmd ->
  Console.WriteLine $"Exit: {cmd.ExitCode.Value}"
  Console.WriteLine $"Stdout:\n{cmd.ReadStdout ()}"
  Console.WriteLine $"Stderr:\n{cmd.ReadStderr ()}"
