#nullable enable

using System.Threading.Tasks;
using Content.Server.Climbing.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests.GameObjects.Components.Movement
{
    [TestFixture]
    [TestOf(typeof(ClimbableComponent))]
    [TestOf(typeof(ClimbingComponent))]
    public class ClimbUnitTest : ContentIntegrationTest
    {
        private const string Prototypes = @"
- type: entity
  name: HumanDummy
  id: HumanDummy
  components:
  - type: Climbing
  - type: Physics

- type: entity
  name: TableDummy
  id: TableDummy
  components:
  - type: Climbable
  - type: Physics
";

        [Test]
        public async Task Test()
        {
            var options = new ServerIntegrationOptions{ExtraPrototypes = Prototypes};
            var server = StartServer(options);

            IEntity human;
            IEntity table;
            ClimbingComponent climbing;

            server.Assert(() =>
            {
                var mapManager = IoCManager.Resolve<IMapManager>();
                mapManager.CreateNewMapEntity(MapId.Nullspace);

                var entityManager = IoCManager.Resolve<IEntityManager>();

                // Spawn the entities
                human = entityManager.SpawnEntity("HumanDummy", MapCoordinates.Nullspace);
                table = entityManager.SpawnEntity("TableDummy", MapCoordinates.Nullspace);

                // Test for climb components existing
                // Players and tables should have these in their prototypes.
                Assert.That(human.TryGetComponent(out climbing!), "Human has no climbing");
                Assert.That(table.TryGetComponent(out ClimbableComponent _), "Table has no climbable");

                // Now let's make the player enter a climbing transitioning state.
                climbing.IsClimbing = true;
                climbing.TryMoveTo(human.Transform.WorldPosition, table.Transform.WorldPosition);
                var body = human.GetComponent<IPhysBody>();
                // TODO: Check it's climbing

                // Force the player out of climb state. It should immediately remove the ClimbController.
                climbing.IsClimbing = false;

            });

            await server.WaitIdleAsync();
        }
    }
}
