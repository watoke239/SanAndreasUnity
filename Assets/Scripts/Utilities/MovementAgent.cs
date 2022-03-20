using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SanAndreasUnity.Utilities
{
    public class MovementAgent : MonoBehaviour
    {
        public NavMeshAgent NavMeshAgent { get; private set; }

        private float m_lastTimeWhenSearchedForPath = 0f;

        /*private List<PathNodeId> m_path = null;
        private int m_pathIndex = -1;
        private bool m_isSearchingForPath = false;*/

        public Vector3? Destination { get; set; } = null;
        private Vector3? m_lastAssignedDestination = null;
        private Vector3? m_lastPositionWhenAssignedDestination = null;

        private float m_lastTimeWhenWarped = 0f;
        private float m_timeSinceSampledOffNavMesh = 0f;

        public Vector3 DesiredVelocity
        {
            get
            {
                if (!m_sampledPosOffNavMesh.HasValue)
                    return this.NavMeshAgent.desiredVelocity;

                Vector3 diff = m_sampledPosOffNavMesh.Value - this.NavMeshAgent.transform.position;
                float distance = diff.magnitude;
                if (distance <= this.StoppingDistance)
                    return Vector3.zero;
                return diff / distance;
            }
        }

        public Vector3? CalculatedDestination { get; private set; } = null;

        private Vector3? m_sampledPosOffNavMesh = null;

        public float StoppingDistance
        {
            get => this.NavMeshAgent.stoppingDistance;
            set => this.NavMeshAgent.stoppingDistance = value;
        }



        void Awake()
        {
            this.NavMeshAgent = this.GetComponentOrThrow<NavMeshAgent>();

            this.NavMeshAgent.updatePosition = false;
            this.NavMeshAgent.updateRotation = false;
            this.NavMeshAgent.updateUpAxis = false;
        }

        void Update()
        {
            /*
                        // check if arrived to next position
                        if (m_path != null && Vector2.Distance(ped.transform.position.ToVec2WithXAndZ(), NodeReader.GetNodeById(m_path[m_pathIndex]).Position.ToVec2WithXAndZ())
                            < NodeReader.GetNodeById(m_path[m_pathIndex]).PathWidth / 2f)
                        {
                            // arrived at next position
                            m_pathIndex ++;
                            if (m_pathIndex >= m_path.Count)
                            {
                                // arrived at destination
                                m_path = null;
                                m_pathIndex = -1;
                                NextMovementPos = null;
                            }
                            else
                            {
                                // move-on to next path node
                                NextMovementPos = NodeReader.GetNodeById(m_path[m_pathIndex]).Position;
                            }
                        }
            */

            /*this.NextMovementPos = ped.NavMeshAgent.hasPath
                ? (Vector3?) ped.NavMeshAgentNextPosition
                : null;*/


            NavMeshAgent agent = this.NavMeshAgent;

            if (!agent.enabled)
            {
                this.ResetParams();
                return;
            }

            Vector3 myPosition = agent.transform.position;

            agent.nextPosition = myPosition;

            Vector3 retreivedNextPosition = agent.nextPosition;

            if (Time.time - m_lastTimeWhenWarped > 1f
                && (retreivedNextPosition.WithXAndZ() != myPosition.WithXAndZ() || !agent.isOnNavMesh))
            {
                m_lastTimeWhenWarped = Time.time;

                bool bWarp = false;
                bool bSetDestination = false;
                
                if (agent.Warp(myPosition))
                {
                    bWarp = true;
                    if (this.Destination.HasValue && agent.isOnNavMesh)
                    {
                        this.SetDestination();
                        bSetDestination = true;
                    }
                }

                //Debug.Log($"warped agent {agent.name} - bWarp {bWarp}, isOnNavMesh {agent.isOnNavMesh}, pos diff {retreivedNextPosition - myPosition}, bSetDestination {bSetDestination}", this);
            }

            // no need to set velocity, it's automatically set by Agent
            //this.NavMeshAgent.velocity = this.Velocity;

            // update calculated destination
            this.CalculatedDestination = agent.hasPath ? agent.destination : (Vector3?)null;

            // if agent is off nav mesh for some time, try to get it back
            if (!agent.isOnNavMesh)
            {
                m_timeSinceSampledOffNavMesh += Time.deltaTime;
                if (m_timeSinceSampledOffNavMesh > 2.5f)
                {
                    // try to get back to nav mesh
                    m_timeSinceSampledOffNavMesh = 0f;

                    m_sampledPosOffNavMesh = this.Destination; // if position can not be sampled, go straight to destination

                    if (NavMesh.SamplePosition(myPosition, out var hit, 150f, agent.areaMask))
                        m_sampledPosOffNavMesh = hit.position;

                    //Debug.Log($"Tried to sample position off nav mesh - agent {agent.name}, sampled pos {m_sampledPosOffNavMesh}, distance {hit.distance}", this);
                }
            }
            else
            {
                m_timeSinceSampledOffNavMesh = 0f;
                m_sampledPosOffNavMesh = null;
            }

            if (!this.Destination.HasValue)
            {
                this.ResetParams();
                return;
            }

            if (Time.time - m_lastTimeWhenSearchedForPath < 0.4f)
                return;

            if (agent.pathPending)
                return;

            if (!agent.isOnNavMesh)
                return;

            if (!m_lastAssignedDestination.HasValue)
            {
                this.SetDestination();
                return;
            }

            // check if target position changed by some delta value (this value should depend on distance to target
            // - if target is too far away, value should be higher)

            Vector3 diffToTarget = this.Destination.Value - myPosition;
            float distanceToTarget = diffToTarget.magnitude;
            Vector3 deltaPos = this.Destination.Value - m_lastAssignedDestination.Value;
            float deltaPosLength = deltaPos.magnitude;

            // we require 10% change, with 1.5 as min
            float requiredPosChange = Mathf.Max(distanceToTarget * 0.1f, 1.5f);

            if (deltaPosLength > requiredPosChange)
            {
                this.SetDestination();
                return;
            }

            // check if angle to target changed by some delta value (eg. 25 degrees)
            // - this will make the ped turn fast in response to target changing movement direction

            Vector3 lastDiffToTarget = m_lastAssignedDestination.Value - m_lastPositionWhenAssignedDestination.Value;
            float angleDelta = Vector3.Angle(this.Destination.Value - m_lastPositionWhenAssignedDestination.Value, lastDiffToTarget);
            if (angleDelta > 25f)
            {
                this.SetDestination();
                return;
            }

            // regularly update path on some higher interval (eg. 5s)
            // - this interval could also depend on distance to target

            // from 5 to 12, with sqrt function, 150 as max distance
            float regularUpdateInterval = 5 + 7 * Mathf.Clamp01(Mathf.Sqrt(Mathf.Min(distanceToTarget, 150f) / 150f));

            if (Time.time - m_lastTimeWhenSearchedForPath > regularUpdateInterval
                && this.Destination.Value != m_lastAssignedDestination.Value)
            {
                this.SetDestination();
                return;
            }

            // handle cases when destination changes by significant amount, but it's not recognized
            // by "delta position" method above

            float deltaInPosition = (this.Destination.Value - m_lastAssignedDestination.Value).magnitude;
            float currentDistance = (m_lastAssignedDestination.Value - myPosition).magnitude;
            if (deltaInPosition > currentDistance)
            {
                Debug.Log($"delta pos higher than current distance - agent {agent.name}, delta {deltaInPosition}, current distance {currentDistance}", this);
                this.SetDestination();
                return;
            }

        }

        void SetDestination()
        {
            NavMeshAgent navMeshAgent = this.NavMeshAgent;

            m_lastTimeWhenSearchedForPath = Time.time;
            m_lastAssignedDestination = this.Destination.Value;
            m_lastPositionWhenAssignedDestination = navMeshAgent.transform.position;
            
            // here we need to sample position on navmesh first, because otherwise agent will fail
            // to calculate path if target position is not on navmesh, and as a result he will be stopped

            // there is a performance problem: if target position is on isolated part of navmesh,
            // path calculation will take too long because the algorithm tries to go through all
            // surrounding nodes, and in the meantime agent stays in place

            // that's why we manually calculate path and assign it to agent - in this case, there is no waiting
            // for path to be calculated asyncly, and agent starts moving immediately. The potential problem
            // is that CalculatePath() can take 1-2 ms.

            if (NavMesh.SamplePosition(this.Destination.Value, out var hit, 100f, navMeshAgent.areaMask))
            {
                var navMeshPath = new NavMeshPath();
                NavMesh.CalculatePath(navMeshAgent.nextPosition, hit.position, navMeshAgent.areaMask, navMeshPath);
                navMeshAgent.path = navMeshPath;

                this.CalculatedDestination = navMeshAgent.hasPath ? navMeshAgent.destination : (Vector3?)null;
            }
            else
            {
                // if position can not be sampled, we stop the agent
                navMeshAgent.ResetPath();
                this.CalculatedDestination = null;
            }
        }

        private void ResetParams()
        {
            m_lastAssignedDestination = null;
            m_lastPositionWhenAssignedDestination = null;
            this.CalculatedDestination = null;

            if (this.NavMeshAgent.hasPath)
                this.NavMeshAgent.ResetPath();
        }

        /*void OnPathFinished(PathfindingManager.PathResult pathResult)
        {
            m_isSearchingForPath = false;
            m_lastTimeWhenSearchedForPath = Time.time;

            if (null == pathResult || !pathResult.IsSuccess)
            {
                m_path = null;
                m_pathIndex = -1;
                NextMovementPos = null;

                return;
            }

            m_path = pathResult.Nodes;
            m_pathIndex = 0;
            NextMovementPos = NodeReader.GetNodeById(m_path[m_pathIndex]).Position;
        }*/
    }
}
