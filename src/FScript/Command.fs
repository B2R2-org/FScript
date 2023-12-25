(*
  FScript - F#-based scripting library

  Copyright (c) SoftSec Lab. @ KAIST, since 2016

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in all
  copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
  SOFTWARE.
*)

namespace B2R2.FScript

open System
open System.IO
open System.Diagnostics
open System.Collections.Concurrent

/// A command to be executed in a pipeline.
[<AllowNullLiteral>]
type Command (prog, args) =
  let proc = new Process ()
  let args = args |> List.map (fun x -> $"\"{x}\"") |> String.concat " "
  let mutable parent = null
  let mutable exitCode = None
  let [<Literal>] BufLen = 8192
  let outBuffer: byte[] = Array.zeroCreate BufLen
  let errBuffer: byte[] = Array.zeroCreate BufLen
  let input = new ConcurrentQueue<byte[]> ()
  let mutable outQueue = new ConcurrentQueue<byte[]> ()
  let mutable errQueue = new ConcurrentQueue<byte[]> ()

  do proc.StartInfo.FileName <- prog
     proc.StartInfo.Arguments <- args
     proc.StartInfo.UseShellExecute <- false
     proc.StartInfo.RedirectStandardOutput <- true
     proc.StartInfo.RedirectStandardError <- true

  new (args: string[]) = Command (args[0], Array.toList args[1..])

  /// The command line string.
  member __.CmdLine with get() = $"{prog} {args}"

  /// The exit code of the command. This is only available after the command is
  /// executed and the command has exited.
  member __.ExitCode with get() = exitCode

  /// Whether the command has exited.
  member __.HasExited with get() = Option.isSome exitCode

  member __.SetPipeOut (inStream, errStream) =
    outQueue <- inStream
    errQueue <- errStream

  /// Connect two commands in a pipeline.
  member __.Connect (src: Command) =
    proc.StartInfo.RedirectStandardInput <- true
    parent <- src
    src.SetPipeOut (input, errQueue)

  member private __.ReadOutput (queue: ConcurrentQueue<byte[]>) =
    [| while not queue.IsEmpty do
         match queue.TryDequeue () with
         | true, bs -> yield bs
         | false, _ -> () |]
    |> Array.concat
    |> Text.Encoding.Default.GetString

  /// Read the standard output of the command. This is only available after the
  /// command is executed in an asynchronous manner, i.e., using `RunAsync`.
  member __.ReadStdout () =
    __.ReadOutput outQueue

  /// Read the standard error of the command. This is only available after the
  /// command is executed in an asynchronous manner, i.e., using `RunAsync`.
  member __.ReadStderr () =
    __.ReadOutput errQueue

  member private __.ReadFromPipe () =
    async {
      if proc.StartInfo.RedirectStandardInput then
        if input.IsEmpty && parent.HasExited then proc.StandardInput.Close ()
        else
          match input.TryDequeue () with
          | true, bs -> proc.StandardInput.BaseStream.Write bs
          | false, _ -> ()
          return! __.ReadFromPipe ()
      else ()
    }

  member private __.ReadFromStream (buffer, stream: Stream) =
    try stream.Read (buffer, 0, BufLen) |> Ok
    with _ -> Error ()

  member private __.WriteOutPipe () =
    async {
      match __.ReadFromStream (outBuffer, proc.StandardOutput.BaseStream) with
      | Ok cnt when cnt > 0 ->
        Array.sub outBuffer 0 cnt |> outQueue.Enqueue
        return! __.WriteOutPipe ()
      | _ -> ()
    }

  member private __.WriteErrPipe () =
    async {
      match __.ReadFromStream (errBuffer, proc.StandardError.BaseStream) with
      | Ok cnt when cnt > 0 ->
        let prefix = $"vvv Err from {prog}\n" |> Text.Encoding.Default.GetBytes
        let suffix = $"^^^ Err from {prog}\n" |> Text.Encoding.Default.GetBytes
        Array.concat [ prefix; Array.sub errBuffer 0 cnt; suffix ]
        |> errQueue.Enqueue
        return! __.WriteErrPipe ()
      | _ -> ()
    }

  member private __.WriteToPipe () =
    [ __.WriteOutPipe (); __.WriteErrPipe () ]
    |> Async.Parallel
    |> Async.Ignore

  /// Run the command asynchronously. All the connected commands in the pipeline
  /// are also executed asynchronously. This is generally slower than `Run`
  /// because it uses ConcurrentQueue to pass data between commands.
  member __.RunAsync () =
    async {
      if isNull parent then () else parent.RunAsync () |> Async.Start
      proc.Start () |> ignore
      [ __.ReadFromPipe (); __.WriteToPipe () ]
      |> Async.Parallel
      |> Async.Ignore
      |> Async.RunSynchronously
      proc.WaitForExit ()
      exitCode <- Some proc.ExitCode
      proc.Close ()
    }

  member private __.Output (out: string, _) =
    Text.Encoding.Default.GetBytes out

  member private __.ReadInput (parentOut: byte[]) =
    if proc.StartInfo.RedirectStandardInput then
      use input = new MemoryStream (parentOut)
      input.CopyTo (proc.StandardInput.BaseStream)
      proc.StandardInput.Close ()
    else ()

  /// Run the command synchronously. All the connected commands in the pipeline
  /// are also executed synchronously.
  member __.Run () =
    let parentOut = if isNull parent then null else parent.Run () |> __.Output
    proc.Start () |> ignore
    __.ReadInput parentOut
    let out = proc.StandardOutput.ReadToEnd ()
    let err = proc.StandardError.ReadToEnd ()
    proc.WaitForExit ()
    exitCode <- Some proc.ExitCode
    proc.Dispose ()
    out, err

[<AutoOpen>]
module Command =
  /// Create a command to be executed in a pipeline.
  let (!) prog args =
    Command (prog, args)

  /// Connect two commands in a pipeline.
  let (=>) (src: Command) (dst: Command) =
    dst.Connect src
    dst

  /// Execute a command and pass it to a continuation.
  let (=|) (cmd: Command) cont =
    cmd.RunAsync () |> Async.RunSynchronously
    cont cmd