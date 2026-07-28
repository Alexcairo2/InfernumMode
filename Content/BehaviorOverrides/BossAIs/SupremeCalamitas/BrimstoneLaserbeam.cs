using System.Collections.Generic;
using System.IO;
using System.Linq;
using CalamityMod;
using CalamityMod.Graphics.Metaballs;
using CalamityMod.NPCs;
using CalamityMod.Projectiles.Magic;
using CalamityMod.Systems.Mechanic;
using InfernumMode.Assets.Effects;
using InfernumMode.Assets.ExtraTextures;
using InfernumMode.Common.Graphics.Primitives;
using Luminance.Core.Graphics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SCalBoss = CalamityMod.NPCs.SupremeCalamitas.SupremeCalamitas;

namespace InfernumMode.Content.BehaviorOverrides.BossAIs.SupremeCalamitas
{
    public class BrimstoneLaserbeam : ModProjectile
    {
        public PrimitiveTrailCopy RayDrawer;

        public ref float LaserLength => ref Projectile.ai[1];

        public const int Lifetime = 360;

        public const float MaxLaserLength = 3330f;

        public override string Texture => "CalamityMod/Projectiles/InvisibleProj";

        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 32;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.hide = true;
            Projectile.timeLeft = 7200;
            Projectile.Calamity().DealsDefenseDamage = true;
            CooldownSlot = ImmunityCooldownID.Bosses;
        }

        public override void SendExtraAI(BinaryWriter writer) => writer.Write(Projectile.rotation);

        public override void ReceiveExtraAI(BinaryReader reader) => Projectile.rotation = reader.ReadSingle();

        public override void AI()
        {
            // Die if SCal is gone.
            if (CalamityGlobalNPC.SCal == -1 || !Main.npc[CalamityGlobalNPC.SCal].active)
            {
                Projectile.Kill();
                return;
            }

            // Grow bigger up to a point.
            Projectile.scale = Clamp(Projectile.scale + 0.15f, 0.05f, 2f);

            // Decide where to position the laserbeam.
            Vector2 circlePointDirection = Main.npc[CalamityGlobalNPC.SCal].Infernum().ExtraAI[2].ToRotationVector2();
            Projectile.velocity = circlePointDirection;
            Projectile.Center = Main.npc[CalamityGlobalNPC.SCal].Center;

            // Update the laser length, treating both real tiles and the SCal arena boundary as valid collision surfaces.
            UpdateLaserLength();

            // Create arms on surfaces.
            if (Main.myPlayer == Projectile.owner)
                CreateLavaOnSurfaces();

            // Create hit effects at the end of the beam.
            if (Main.myPlayer == Projectile.owner)
                CreateTileHitEffects();

            Projectile.hide = true;

            // Make the beam cast light along its length. The brightness of the light is reliant on the scale of the beam.
            DelegateMethods.v3_1 = Color.DarkViolet.ToVector3() * Projectile.scale * 0.4f;
            Utils.PlotTileLine(Projectile.Center, Projectile.Center + Projectile.velocity * LaserLength, Projectile.width * Projectile.scale, DelegateMethods.CastLight);
        }

        public void UpdateLaserLength()
        {
            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitY);

            // Existing collision against real tiles.
            float[] laserLengthSamplePoints = new float[24];

            Collision.LaserScan(Projectile.Center, direction, Projectile.scale * 24f, MaxLaserLength, laserLengthSamplePoints);

            float tileCollisionLength = laserLengthSamplePoints.Average();

            // Also treat the SCal arena boundary as a solid surface.
            float arenaCollisionLength = CalculateArenaCollisionLength(Projectile.Center, direction, Projectile.scale * 12f);

            LaserLength = MathHelper.Min(tileCollisionLength, arenaCollisionLength);
        }

        public void CreateLavaOnSurfaces()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitY);
            Vector2 endOfLaser = Projectile.Center + direction * LaserLength;
            RancorLavaMetaball.SpawnParticle(endOfLaser + Main.rand.NextVector2Circular(10f, 10f) + direction * 40f, 320f);
        }

        public void CreateTileHitEffects()
        {
            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitY);

            float effectDistance = MathHelper.Max(LaserLength - Main.rand.NextFloat(12f, 72f), 0f);

            Vector2 endOfLaser = Projectile.Center + direction * effectDistance;

            if (Main.rand.NextBool(6))
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), endOfLaser, Main.rand.NextVector2Circular(4f, 8f), ModContent.ProjectileType<RancorFog>(), 0, 0f, Projectile.owner);

            if (Main.rand.NextBool(2))
            {
                int type = ModContent.ProjectileType<RancorSmallCinder>();
                float cinderSpeed = Main.rand.NextFloat(2f, 6f);
                Vector2 cinderVelocity = Vector2.Lerp(-direction, -Vector2.UnitY, 0.45f).RotatedByRandom(0.72f) * cinderSpeed;
                Projectile.NewProjectile(Projectile.GetSource_FromAI(), endOfLaser, cinderVelocity, type, 0, 0f, Projectile.owner);
            }
        }

        private static float CalculateArenaCollisionLength(Vector2 origin, Vector2 direction, float beamRadius)
        {
            if (!Main.npc.IndexInRange(CalamityGlobalNPC.SCal))
                return MaxLaserLength;

            NPC scal = Main.npc[CalamityGlobalNPC.SCal];
            if (!scal.active)
                return MaxLaserLength;

            ArenaWallSystem.Box arenaBox = scal.ModNPC<SCalBoss>().ArenaBox;

            if (arenaBox is null)
                return MaxLaserLength;

            // Inset the valid interior by half the beam width. This makes the edge of the laser collide with the arena rather than its center.
            float left = arenaBox.TopLeft.X + beamRadius;
            float right = arenaBox.BottomRight.X - beamRadius;
            float top = arenaBox.TopLeft.Y + beamRadius;
            float bottom = arenaBox.BottomRight.Y - beamRadius;

            if (right <= left || bottom <= top)
                return MaxLaserLength;

            float collisionDistance = MaxLaserLength;
            const float epsilon = 0.0001f;

            // Test the vertical wall in the beam's travel direction.
            if (Abs(direction.X) > epsilon)
            {
                float wallX = direction.X > 0f ? right : left;
                float distance = (wallX - origin.X) / direction.X;

                if (distance >= 0f && distance <= MaxLaserLength)
                {
                    float intersectionY = origin.Y + direction.Y * distance;

                    if (intersectionY >= top && intersectionY <= bottom)
                    {
                        collisionDistance = MathHelper.Min(collisionDistance, distance);
                    }
                }
            }

            // Test the horizontal wall in the beam's travel direction.
            if (Abs(direction.Y) > epsilon)
            {
                float wallY = direction.Y > 0f ? bottom : top;
                float distance = (wallY - origin.Y) / direction.Y;

                if (distance >= 0f && distance <= MaxLaserLength)
                {
                    float intersectionX = origin.X + direction.X * distance;

                    if (intersectionX >= left && intersectionX <= right)
                    {
                        collisionDistance = MathHelper.Min(collisionDistance, distance);
                    }
                }
            }

            return Clamp(collisionDistance, 0f, MaxLaserLength);
        }

        private float PrimitiveWidthFunction(float completionRatio) => Projectile.scale * 10f;

        private Color PrimitiveColorFunction(float completionRatio)
        {
            Color vibrantColor = Color.Lerp(Color.Blue, Color.Red, Cos(Main.GlobalTimeWrappedHourly * 0.67f - completionRatio / LaserLength * 29f) * 0.5f + 0.5f);
            float opacity = Projectile.Opacity * Utils.GetLerpValue(0.97f, 0.9f, completionRatio, true) *
                Utils.GetLerpValue(0f, Clamp(15f / LaserLength, 0f, 0.5f), completionRatio, true) *
                Pow(Utils.GetLerpValue(60f, 270f, LaserLength, true), 3f);
            return Color.Lerp(vibrantColor, Color.White, 0.3f) * opacity * 2f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Vector2[] basePoints = new Vector2[24];
            for (int i = 0; i < basePoints.Length; i++)
                basePoints[i] = Projectile.Center + Projectile.velocity * i / (basePoints.Length - 1f) * LaserLength;

            Vector2 overallOffset = -Main.screenPosition;
            InfernumEffectsRegistry.FlameVertexShader.SetTexture(InfernumTextureRegistry.BlurryPerlinNoise, 1);
            InfernumEffectsRegistry.FlameVertexShader.TrySetParameter("globalTime", Main.GlobalTimeWrappedHourly);
            InfernumEffectsRegistry.FlameVertexShader.TrySetParameter("uSaturation", 1f);

            PrimitiveRenderer.RenderTrail(basePoints, new(PrimitiveWidthFunction, PrimitiveColorFunction, null, true, false, InfernumEffectsRegistry.FlameVertexShader), 64);
            return false;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float _ = 0f;
            float width = PrimitiveWidthFunction(0.4f);

            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitY);

            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, Projectile.Center + direction * LaserLength, width, ref _);
        }

        public override void DrawBehind(int index, List<int> drawCacheProjsBehindNPCsAndTiles, List<int> drawCacheProjsBehindNPCs, List<int> drawCacheProjsBehindProjectiles, List<int> drawCacheProjsOverWiresUI, List<int> overWiresUI)
        {
            drawCacheProjsBehindNPCsAndTiles.Add(index);
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool? CanDamage() => Projectile.timeLeft < 7198;
    }
}
