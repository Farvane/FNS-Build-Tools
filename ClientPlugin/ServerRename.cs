using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Localization;

namespace Farvane.BuildTools;

/// <summary>
/// Renames first-person-selected blocks on the in-process server.
/// </summary>
/// <remarks>
/// F-selection holds client replicas, and FunctionalBlockComponent.DisplayName is a sync property the client may
/// not set. The vanilla Control Panel renames through a streamed detail model whose server half sets the server
/// block's DisplayName. That only works while the Control Panel is open (closing it detaches the model), so this
/// does the server half directly: each client replica is paired to its server entity through the in-process
/// ReplayContext, and the write is posted to the server with InProcessReplicationSessionComponent.PostInvoke,
/// which runs the action on the other side of the in-process replication. Single-player or host only.
/// </remarks>
internal static class ServerRename
{
    // These types are internal to VRage.Multiplayer, so they are reached by name.
    private const string Namespace = "Keen.VRage.Multiplayer.Replications.InProcess.";
    private static readonly Type? ReplicationType = AccessTools.TypeByName(Namespace + "InProcessReplicationSessionComponent");
    private static readonly Type? SceneContextType = AccessTools.TypeByName(Namespace + "SceneContext");
    private static readonly MethodInfo? IsServer = ReplicationType is null ? null : AccessTools.PropertyGetter(ReplicationType, "IsServer");
    private static readonly MethodInfo? Replay = ReplicationType is null ? null : AccessTools.PropertyGetter(ReplicationType, "ReplayContext");
    private static readonly MethodInfo? PostInvoke = ReplicationType is null ? null : AccessTools.Method(ReplicationType, "PostInvoke");
    private static readonly MethodInfo? TranslateLocalToRemote = Replay is null ? null
        : AccessTools.Method(Replay.ReturnType, "TranslateLocalToRemote", new[] { typeof(object) });

    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Validates every block, then posts all renames to the server in one action. Throws before anything changes.</summary>
    internal static string Apply(IReadOnlyList<(Entity Entity, string Name)> renames)
    {
        if (renames.Count == 0) return "Nothing to rename.";
        if (ReplicationType is null || SceneContextType is null || IsServer is null || Replay is null || PostInvoke is null || TranslateLocalToRemote is null)
            throw new NotSupportedException("The in-process replication API changed in this game build; renaming first-person selections is unavailable.");

        var replication = renames[0].Entity.GetSession().TryGet(ReplicationType)
            ?? throw new NotSupportedException("Renaming first-person selections needs single-player or a hosted game.");
        if ((bool)IsServer.Invoke(replication, null)!)
            throw new NotSupportedException("The selected blocks are not client replicas; renaming them here is unsupported.");
        var replay = Replay.Invoke(replication, null)
            ?? throw new NotSupportedException("The in-process replication context is unavailable.");

        var jobs = renames.Select(rename => new Job(rename.Entity, ServerEntityOf(replay, rename.Entity), rename.Name)).ToList();
        var action = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(SceneContextType),
            new Batch(jobs), typeof(Batch).GetMethod(nameof(Batch.Run))!.MakeGenericMethod(SceneContextType));
        PostInvoke.Invoke(replication, new object[] { action });
        ScheduleCheck(jobs);
        Diag.Info("Sent " + jobs.Count + " rename(s) to the server.");
        return "Renaming " + jobs.Count + " block(s)…";
    }

    private static Entity ServerEntityOf(object replay, Entity client)
    {
        try
        {
            return TranslateLocalToRemote!.Invoke(replay, new object[] { client }) as Entity
                ?? throw new InvalidOperationException("The selected block " + client.DebugName + " has no server counterpart.");
        }
        catch (TargetInvocationException error) when (error.InnerException is KeyNotFoundException)
        {
            throw new InvalidOperationException("The selected block " + client.DebugName + " has no server counterpart. Reselect it and try again.");
        }
    }

    /// <summary>After replication, confirms the client replicas show the new names and logs the result.</summary>
    private static void ScheduleCheck(IReadOnlyList<Job> jobs)
    {
        var timer = new DispatcherTimer { Interval = CheckDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var shown = jobs.Count(job => job.Client.TryGet<FunctionalBlockComponent>(default)?.DisplayName.RawText == job.Escaped);
                Diag.Info("Rename check: " + shown + " of " + jobs.Count + " block(s) show the new name.");
            }
            catch (Exception error) { Diag.Failed("Rename check", error); }
        };
        timer.Start();
    }

    private sealed class Job
    {
        internal Job(Entity client, Entity server, string name)
        {
            Client = client;
            Server = server;
            Name = name;
            // Same escaping as the vanilla Control Panel (BlockVisualsBlockDetailModelServer.OnPropertyChanged).
            Escaped = TextManager.EscapeInputString(name);
        }

        internal Entity Client { get; }
        internal Entity Server { get; }
        internal string Name { get; }
        internal string Escaped { get; }
    }

    private sealed class Batch
    {
        private readonly IReadOnlyList<Job> _jobs;
        internal Batch(IReadOnlyList<Job> jobs) => _jobs = jobs;

        /// <summary>Runs on the in-process server. The context argument is the server's SceneContext (unused).</summary>
        public void Run<TContext>(TContext context)
        {
            var renamed = 0;
            foreach (var job in _jobs)
            {
                try
                {
                    var block = job.Server.TryGet<FunctionalBlockComponent>(default);
                    if (block is null)
                    {
                        Diag.Info("Server rename skipped: " + job.Server.DebugName + " is not a functional block.");
                        continue;
                    }
                    block.DisplayName = job.Escaped;
                    renamed++;
                }
                catch (Exception error) { Diag.Failed("Server rename of " + job.Server.DebugName, error); }
            }
            Diag.Info("Server renamed " + renamed + " of " + _jobs.Count + " block(s).");
        }
    }
}
