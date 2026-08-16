using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using RescuAR.AR;
using System;

namespace RescuAR
{
    public class MyScene : Scene
    {
        public override void RegisterManagers()
        {
            base.RegisterManagers();

            this.Managers.AddManager(
                new global::Evergine.Bullet.BulletPhysicManager3D());
        }

        protected override void CreateScene()
        {
            base.CreateScene();

            this.Managers.EntityManager.Add(
                ARCameraBackgroundFactory.Create());

            /*
             * Attach ARCore pose control to the existing Camera entity loaded
             * from MyScene.wescene. FreeCamera3D must remain removed so only
             * ARCore controls the camera transform during the test.
             */
            Entity? cameraEntity =
                this.Managers.EntityManager.Find(
                    "Camera");

            if (cameraEntity is null)
            {
                throw new InvalidOperationException(
                    "The scene does not contain an entity named 'Camera'.");
            }

            Entity? capsuleEntity =
                this.Managers.EntityManager.Find(
                    "capsule");

            if (capsuleEntity is null)
            {
                throw new InvalidOperationException(
                    "The scene does not contain an entity named 'capsule'.");
            }

            MaterialComponent? capsuleMaterialComponent =
                capsuleEntity.FindComponent<MaterialComponent>();

            if (capsuleMaterialComponent?.Material is null)
            {
                throw new InvalidOperationException(
                    "The capsule does not contain a usable MaterialComponent.");
            }

            /*
             * Milestone 3:
             * Build a small hardcoded cyan ground route using the same proven
             * Standard-material/render-layer path as the capsule.
             */
            Entity routeEntity =
                ARRouteRenderer.Create(
                    capsuleMaterialComponent.Material);

            this.Managers.EntityManager.Add(
                routeEntity);

            /*
             * Initialize the AR spatial renderer directly from the loaded
             * scene entities. MyApplication.DrawFrame() applies the newest
             * coherent ARCore snapshot immediately before rendering.
             */
            ARCameraSpatialController.Initialize(
                cameraEntity,
                capsuleEntity,
                routeEntity);
        }
    }
}
