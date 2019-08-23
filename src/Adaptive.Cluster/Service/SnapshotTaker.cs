﻿using System.Threading;
using Adaptive.Aeron;
using Adaptive.Aeron.Exceptions;
using Adaptive.Aeron.LogBuffer;
using Adaptive.Agrona.Concurrent;
using Adaptive.Cluster.Codecs;

namespace Adaptive.Cluster.Service
{
    /// <summary>
    /// Based class of common functions required to take a snapshot of cluster state.
    /// </summary>
    public class SnapshotTaker
    {
        protected static readonly int ENCODED_MARKER_LENGTH =
            MessageHeaderEncoder.ENCODED_LENGTH + SnapshotMarkerEncoder.BLOCK_LENGTH;

        protected readonly BufferClaim bufferClaim = new BufferClaim();
        protected readonly MessageHeaderEncoder messageHeaderEncoder = new MessageHeaderEncoder();
        protected readonly Publication publication;
        protected readonly IIdleStrategy idleStrategy;
        protected readonly AgentInvoker aeronAgentInvoker;
        private readonly SnapshotMarkerEncoder snapshotMarkerEncoder = new SnapshotMarkerEncoder();

        public SnapshotTaker(Publication publication, IIdleStrategy idleStrategy, AgentInvoker aeronAgentInvoker)
        {
            this.publication = publication;
            this.idleStrategy = idleStrategy;
            this.aeronAgentInvoker = aeronAgentInvoker;
        }

        public void MarkBegin(
            long snapshotTypeId,
            long logPosition,
            long leadershipTermId,
            int snapshotIndex,
            ClusterTimeUnit timeUnit,
            int appVersion)
        {
            MarkSnapshot(
                snapshotTypeId, logPosition, leadershipTermId, snapshotIndex, SnapshotMark.BEGIN, timeUnit, appVersion);
        }

        public void MarkEnd(
            long snapshotTypeId,
            long logPosition,
            long leadershipTermId,
            int snapshotIndex,
            ClusterTimeUnit timeUnit,
            int appVersion)
        {
            MarkSnapshot(
                snapshotTypeId, logPosition, leadershipTermId, snapshotIndex, SnapshotMark.END, timeUnit, appVersion);
        }

        public void MarkSnapshot(
            long snapshotTypeId,
            long logPosition,
            long leadershipTermId,
            int snapshotIndex,
            SnapshotMark snapshotMark,
            ClusterTimeUnit timeUnit,
            int appVersion)
        {
            idleStrategy.Reset();
            while (true)
            {
                long result = publication.TryClaim(ENCODED_MARKER_LENGTH, bufferClaim);
                if (result > 0)
                {
                    snapshotMarkerEncoder
                        .WrapAndApplyHeader(bufferClaim.Buffer, bufferClaim.Offset, messageHeaderEncoder)
                        .TypeId(snapshotTypeId)
                        .LogPosition(logPosition)
                        .LeadershipTermId(leadershipTermId)
                        .Index(snapshotIndex)
                        .Mark(snapshotMark)
                        .TimeUnit(timeUnit)
                        .AppVersion(appVersion);

                    bufferClaim.Commit();
                    break;
                }

                CheckResultAndIdle(result);
            }
        }

        protected static void CheckInterruptedStatus()
        {
            try
            {
                Thread.Sleep(0);
            }
            catch (ThreadInterruptedException)
            {
                throw new AgentTerminationException("unexpected interrupt during operation");
            }
        }

        protected static void CheckResult(long result)
        {
            if (result == Publication.NOT_CONNECTED || result == Publication.CLOSED ||
                result == Publication.MAX_POSITION_EXCEEDED)
            {
                throw new AeronException("unexpected publication state: " + result);
            }
        }

        protected void CheckResultAndIdle(long result)
        {
            CheckResult(result);
            CheckInterruptedStatus();
            InvokeAgentClient();
            idleStrategy.Idle();
        }

        protected void InvokeAgentClient()
        {
            aeronAgentInvoker?.Invoke();
        }
    }
}