using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Localization;
using Keen.VRage.Multiplayer.SecureIds;

namespace Farvane.BuildTools;

/// <summary>A block selected in the vanilla Control Panel, resolved to its server entity while the panel was open.</summary>
/// <remarks>
/// Panel rows carry a per-client secure <see cref="EntityId"/> that the server drops when the panel closes, so the id
/// is turned into the server entity right away (on the server thread, see <see cref="ServerRename.Resolve"/>).
/// </remarks>
internal sealed class PanelBlock
{
    private Entity? _server;
    private volatile bool _resolved;

    internal PanelBlock(string name, EntityId id, Session clientSession)
    {
        Name = name;
        Id = id;
        ClientSession = clientSession;
    }

    internal string Name { get; }
    internal EntityId Id { get; }
    internal Session ClientSession { get; }
    internal bool Resolved => _resolved;
    internal Entity? Server => _resolved ? _server : null;

    /// <summary>Called once on the server thread.</summary>
    internal void Complete(Entity? server)
    {
        _server = server;
        _resolved = true; // volatile write publishes _server
    }
}

/// <summary>
/// Renames selected blocks on the in-process server.
/// </summary>
/// <remarks>
/// FunctionalBlockComponent.DisplayName is a sync property the client may not set. The vanilla Control Panel renames
/// through a streamed detail model whose server half sets the server block's DisplayName, but only while the panel is
/// open (closing it detaches the model). This does the server half directly: each block is mapped to its server
/// entity (an F-selected client replica through the in-process ReplayContext, a Control Panel row through the server's
/// secure-id table) and the write is posted with InProcessReplicationSessionComponent.PostInvoke, which runs the action
/// on the other side of the in-process replication. Single-player or host only.
/// </remarks>
internal static class ServerRename
{
    // These types are internal to VRage.Multiplayer, so they are reached by name.
    private const string Namespace = "Keen.VRage.Multiplayer.Replications.InProcess.";
    private static readonly Type? ReplicationType = AccessTools.TypeByName(Namespace + "InProcessReplicationSessionComponent");
    private static readonly Type? SceneContextType = AccessTools.TypeByName(Namespace + "SceneContext");
    private static readonly FieldInfo? SceneSessionComponent = SceneContextType is null ? null : AccessTools.Field(SceneContextType, "SessionComponent");
    private static readonly MethodInfo? IsServer = ReplicationType is null ? null : AccessTools.PropertyGetter(ReplicationType, "IsServer");
    private static readonly MethodInfo? Replay = ReplicationType is null ? null : AccessTools.PropertyGetter(ReplicationType, "ReplayContext");
    private static readonly MethodInfo? PostInvoke = ReplicationType is null ? null : AccessTools.Method(ReplicationType, "PostInvoke");
    private static readonly MethodInfo? TranslateLocalToRemote = Replay is null ? null
        : AccessTools.Method(Replay.ReturnType, "TranslateLocalToRemote", new[] { typeof(object) });
    private static readonly MethodInfo? TranslateRemoteToLocal = Replay is null ? null
        : AccessTools.Method(Replay.ReturnType, "TranslateObject", new[] { typeof(object) });

    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1.5);

    private static bool Available => ReplicationType is not null && SceneContextType is not null && SceneSessionComponent is not null
        && IsServer is not null && Replay is not null && PostInvoke is not null && TranslateLocalToRemote is not null && TranslateRemoteToLocal is not null;

    /// <summary>
    /// Looks up Control Panel ids on the server while the panel is open. Returns false (blocks stay unresolved)
    /// when there is no in-process server.
    /// </summary>
    internal static bool Resolve(Session clientSession, IReadOnlyList<PanelBlock> blocks)
    {
        if (blocks.Count == 0 || !Available) return false;
        var replication = clientSession.TryGet(ReplicationType!);
        if (replication is null || (bool)IsServer!.Invoke(replication, null)!) return false;
        Post(replication, new Lookup(blocks));
        return true;
    }

    /// <summary>Validates every block, then posts all renames to the server in one action. Throws before anything changes.</summary>
    internal static string Apply(IReadOnlyList<(object Target, string Name)> renames)
    {
        if (renames.Count == 0) return "Nothing to rename.";
        if (!Available)
            throw new NotSupportedException("The in-process replication API changed in this game build; renaming is unavailable.");

        var session = renames[0].Target switch
        {
            Entity entity => entity.GetSession(),
            PanelBlock block => block.ClientSession,
            _ => throw new InvalidOperationException("A selected block is no longer available."),
        };
        var replication = session.TryGet(ReplicationType!)
            ?? throw new NotSupportedException("Renaming needs single-player or a hosted game.");
        if ((bool)IsServer!.Invoke(replication, null)!)
            throw new NotSupportedException("The selected blocks are not client replicas; renaming them here is unsupported.");
        var replay = Replay!.Invoke(replication, null)
            ?? throw new NotSupportedException("The in-process replication context is unavailable.");

        var jobs = renames.Select(rename => rename.Target switch
        {
            Entity client => new Job(client, ServerEntityOf(replay, client), rename.Name),
            PanelBlock block => new Job(ClientEntityOf(replay, ServerEntityOf(block)), ServerEntityOf(block), rename.Name),
            _ => throw new InvalidOperationException("A selected block is no longer available."),
        }).ToList();
        Post(replication, new Batch(jobs));
        ScheduleCheck(jobs);
        Diag.Info("Sent " + jobs.Count + " rename(s) to the server.");
        return "Renaming " + jobs.Count + " block(s)…";
    }

    private static void Post(object replication, object job)
    {
        var run = job.GetType().GetMethod("Run")!.MakeGenericMethod(SceneContextType!);
        var action = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(SceneContextType!), job, run);
        PostInvoke!.Invoke(replication, new object[] { action });
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

    private static Entity ServerEntityOf(PanelBlock block)
    {
        if (!block.Resolved)
            throw new InvalidOperationException("The Control Panel selection is still being looked up. Try again in a moment.");
        return block.Server
            ?? throw new InvalidOperationException("\"" + block.Name + "\" could not be found on the server. Select it again in the Control Panel.");
    }

    /// <summary>The client replica of a server entity, for the follow-up check only (null when unpaired).</summary>
    private static Entity? ClientEntityOf(object replay, Entity server)
    {
        try { return TranslateRemoteToLocal!.Invoke(replay, new object[] { server }) as Entity; }
        catch (TargetInvocationException) { return null; }
    }

    /// <summary>After replication, confirms the client replicas show the new names and logs the result.</summary>
    private static void ScheduleCheck(IReadOnlyList<Job> jobs)
    {
        var checkable = jobs.Where(job => job.Client is not null).ToList();
        if (checkable.Count == 0) return;
        var timer = new DispatcherTimer { Interval = CheckDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var shown = checkable.Count(job => job.Client!.TryGet<FunctionalBlockComponent>(default)?.DisplayName.RawText == job.Escaped);
                Diag.Info("Rename check: " + shown + " of " + checkable.Count + " block(s) show the new name.");
            }
            catch (Exception error) { Diag.Failed("Rename check", error); }
        };
        timer.Start();
    }

    private sealed class Job
    {
        internal Job(Entity? client, Entity server, string name)
        {
            Client = client;
            Server = server;
            // Same escaping as the vanilla Control Panel (BlockVisualsBlockDetailModelServer.OnPropertyChanged).
            Escaped = TextManager.EscapeInputString(name);
        }

        internal Entity? Client { get; }
        internal Entity Server { get; }
        internal string Escaped { get; }
    }

    /// <summary>Runs on the in-process server: writes the names.</summary>
    private sealed class Batch
    {
        private readonly IReadOnlyList<Job> _jobs;
        internal Batch(IReadOnlyList<Job> jobs) => _jobs = jobs;

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

    /// <summary>Runs on the in-process server: turns Control Panel ids into server entities.</summary>
    private sealed class Lookup
    {
        private readonly IReadOnlyList<PanelBlock> _blocks;
        internal Lookup(IReadOnlyList<PanelBlock> blocks) => _blocks = blocks;

        public void Run<TContext>(TContext context)
        {
            ISecureEntityIds? ids = null;
            try { ids = (SceneSessionComponent!.GetValue(context) as Component)?.GetSession().TryGet<ISecureEntityIds>(); }
            catch (Exception error) { Diag.Failed("Control Panel lookup", error); }
            var missing = 0;
            foreach (var block in _blocks)
            {
                Entity? server = null;
                try { server = ids?.TryGetEntity(block.Id, null, null); }
                catch (Exception error) { Diag.Failed("Control Panel lookup of " + block.Name, error); }
                if (server is null) missing++;
                block.Complete(server);
            }
            if (missing > 0) Diag.Info("Control Panel lookup: " + missing + " of " + _blocks.Count + " block(s) not found on the server.");
        }
    }
}
