using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Mind.Components;
using Content.Server.Power.Components;
using Content.Server.Chemistry.Components.SolutionManager;
using Content.Shared.Tag;
using Robust.Server.Bql;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Bql
{
    public class QuerySelectors
    {
        [RegisterBqlQuerySelector]
        public class MindfulQuerySelector : BqlQuerySelector
        {
            public override string Token => "mindful";

            public override QuerySelectorArgument[] Arguments => Array.Empty<QuerySelectorArgument>();

            public override IEnumerable<EntityUid> DoSelection(IEnumerable<EntityUid> input,
                IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return input.Where(e =>
                {
                    if (entityManager.TryGetComponent<MindComponent>(e, out var mind))
                        return (mind.Mind?.VisitingEntity?.Uid == e) ^ isInverted;

                    return isInverted;
                });
            }

            public override IEnumerable<EntityUid> DoInitialSelection(IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {

                return DoSelection(
                    entityManager.EntityQuery<MindComponent>().Select(x => x.Owner.Uid),
                    arguments, isInverted, entityManager);
            }
        }

        [RegisterBqlQuerySelector]
        public class TaggedQuerySelector : BqlQuerySelector
        {
            public override string Token => "tagged";

            public override QuerySelectorArgument[] Arguments => new [] { QuerySelectorArgument.String };

            public override IEnumerable<EntityUid> DoSelection(IEnumerable<EntityUid> input, IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return input.Where(e =>
                    (entityManager.TryGetComponent<TagComponent>(e, out var tag) &&
                    tag.Tags.Contains((string) arguments[0])) ^ isInverted);
            }

            public override IEnumerable<EntityUid> DoInitialSelection(IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return DoSelection(entityManager.EntityQuery<TagComponent>().Select(x => x.Owner.Uid), arguments,
                    isInverted, entityManager);

            }
        }

        [RegisterBqlQuerySelector]
        public class AliveQuerySelector : BqlQuerySelector
        {
            public override string Token => "alive";

            public override QuerySelectorArgument[] Arguments => Array.Empty<QuerySelectorArgument>();

            public override IEnumerable<EntityUid> DoSelection(IEnumerable<EntityUid> input, IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return input.Where(e =>
                    (entityManager.TryGetComponent<MindComponent>(e, out var mind) &&
                    !(mind.Mind?.CharacterDeadPhysically ?? false)) ^ isInverted);
            }

            public override IEnumerable<EntityUid> DoInitialSelection(IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return DoSelection(entityManager.EntityQuery<MindComponent>().Select(x => x.Owner.Uid), arguments,
                    isInverted, entityManager);
            }
        }

        [RegisterBqlQuerySelector]
        public class HasReagentQuerySelector : BqlQuerySelector
        {
            public override string Token => "hasreagent";

            public override QuerySelectorArgument[] Arguments => new [] { QuerySelectorArgument.String };

            public override IEnumerable<EntityUid> DoSelection(IEnumerable<EntityUid> input, IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                var reagent = (string) arguments[0];
                return input.Where(e =>
                {
                    if (entityManager.TryGetComponent<SolutionContainerManagerComponent>(e, out var solutionContainerManagerComponent))
                    {
                        return solutionContainerManagerComponent.Solutions
                            .Any(solution => solution.Value.ContainsReagent(reagent)) ^ isInverted;
                    }

                    return isInverted;
                });
            }

            public override IEnumerable<EntityUid> DoInitialSelection(IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return DoSelection(entityManager.EntityQuery<SolutionContainerManagerComponent>().Select(x => x.Owner.Uid), arguments,
                    isInverted, entityManager);
            }
        }

        [RegisterBqlQuerySelector]
        public class ApcPoweredQuerySelector : BqlQuerySelector
        {
            public override string Token => "apcpowered";

            public override QuerySelectorArgument[] Arguments => Array.Empty<QuerySelectorArgument>();

            public override IEnumerable<EntityUid> DoSelection(IEnumerable<EntityUid> input, IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return input.Where(e =>
                    entityManager.TryGetComponent<ApcPowerReceiverComponent>(e, out var apcPowerReceiver)
                        ? apcPowerReceiver.Powered ^ isInverted
                        : isInverted);
            }

            public override IEnumerable<EntityUid> DoInitialSelection(IReadOnlyList<object> arguments, bool isInverted, IEntityManager entityManager)
            {
                return DoSelection(entityManager.EntityQuery<ApcPowerReceiverComponent>().Select(x => x.Owner.Uid), arguments,
                    isInverted, entityManager);
            }
        }
    }
}
