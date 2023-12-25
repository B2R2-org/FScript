namespace B2R2.FScript

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic

/// Internal message types used by TaskQueue.
type TaskMessage =
  | AddTask of Command
  | FetchTask of AsyncReplyChannel<Command>
  | ReturnSuccess of string * string * string
  | ReturnFailure of string * string * string
  | CheckExit of AsyncReplyChannel<bool>

type TaskLog = {
  CmdLine: string
  OutLog: string
  ErrLog: string
}

/// TaskQueue is a simple task queue that runs commands in parallel while
/// maximizing CPU utilization.
type TaskQueue (?numCores: int) =
  let printStatus success failure = Console.Write $"\r{success},{failure}"
  let queue = Queue ()
  let outs = Queue<TaskLog> ()
  let errs = Queue<TaskLog> ()

  let taskQueue =
    MailboxProcessor.Start (fun inbox ->
      let rec loop cnt success failure = async {
        match! inbox.Receive () with
        | AddTask cmd ->
          queue.Enqueue cmd
          return! loop (cnt + 1) success failure
        | FetchTask ch when queue.Count = 0 ->
          ch.Reply null
          return! loop cnt success failure
        | FetchTask ch ->
          queue.Dequeue () |> ch.Reply
          return! loop cnt success failure
        | ReturnSuccess (cmdLine, out, err) ->
          printStatus success failure
          { CmdLine = cmdLine; OutLog = out; ErrLog = err }
          |> outs.Enqueue
          return! loop (cnt - 1) (success + 1) failure
        | ReturnFailure (cmdLine, out, err) ->
          printStatus success failure
          { CmdLine = cmdLine; OutLog = out; ErrLog = err }
          |> errs.Enqueue
          return! loop (cnt - 1) success (failure + 1)
        | CheckExit ch when cnt = 0 ->
          printStatus success failure
          Console.WriteLine ()
          ch.Reply true
        | CheckExit ch ->
          printStatus success failure
          ch.Reply false
          return! loop cnt success failure
      }
      loop 0 0 0
    )

  let work () =
    while true do
      let cmd = taskQueue.PostAndReply (fun ch -> FetchTask ch)
      if isNull cmd then ()
      else
        try
          let out, err = cmd.Run ()
          match cmd.ExitCode with
          | Some 0 -> taskQueue.Post (ReturnSuccess (cmd.CmdLine, out, err))
          | _ -> taskQueue.Post (ReturnFailure (cmd.CmdLine, out, err))
        with e ->
          taskQueue.Post (ReturnFailure (cmd.CmdLine, "", $"{e.ToString ()}"))

  let numWorkers = defaultArg numCores Environment.ProcessorCount

  let _ = Array.init numWorkers (fun _ -> Task.Run work)

  /// Add a command to the task queue.
  member __.AddTask cmd =
    taskQueue.Post (AddTask cmd)

  /// Wait until all the commands in the task queue are finished.
  member __.Wait () =
    match taskQueue.PostAndReply (fun ch -> CheckExit ch) with
    | false -> Thread.Sleep 100; __.Wait ()
    | true -> ()

  member __.Outputs with get() = outs |> Seq.toArray

  member __.Errors with get() = errs |> Seq.toArray
