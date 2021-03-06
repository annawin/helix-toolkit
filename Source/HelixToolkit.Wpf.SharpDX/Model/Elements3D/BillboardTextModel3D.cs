﻿using System.Windows;
using System.Collections.Generic;
using System.Linq;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.Diagnostics;

namespace HelixToolkit.Wpf.SharpDX
{
    public class BillboardTextModel3D : MaterialGeometryModel3D
    {
        #region Private Class Data Members

        private EffectVectorVariable vViewport;
        private EffectScalarVariable bHasBillboardTexture;
        private ShaderResourceView billboardTextureView;
        private ShaderResourceView billboardAlphaTextureView;
        private EffectShaderResourceVariable billboardTextureVariable;
        private EffectShaderResourceVariable billboardAlphaTextureVariable;
        private EffectScalarVariable bHasBillboardAlphaTexture;
        private BillboardType billboardType;
        private BillboardVertex[] vertexArrayBuffer;
        #endregion

        #region Overridable Methods
        public override int VertexSizeInBytes
        {
            get
            {
                return BillboardVertex.SizeInBytes;
            }
        }
        /// <summary>
        /// Initial implementation of hittest for billboard. Needs further improvement.
        /// </summary>
        /// <param name="rayWS"></param>
        /// <param name="hits"></param>
        /// <returns></returns>
        public override bool HitTest(Ray rayWS, ref List<HitTestResult> hits)
        {
            if (this.Visibility == Visibility.Collapsed || this.Visibility == Visibility.Hidden)
            {
                return false;
            }
            if (this.IsHitTestVisible == false)
            {
                return false;
            }

            var g = this.Geometry as IBillboardText;
            var h = false;
            var result = new HitTestResult();
            result.Distance = double.MaxValue;
            Viewport3DX viewport;

            if ((viewport = FindVisualAncestor<Viewport3DX>(this.renderHost as DependencyObject)) == null || g.Width == 0 || g.Height == 0)
            {
                return false;
            }

            if (g != null)
            {
                var visualToScreen = viewport.GetViewProjectionMatrix() * viewport.GetViewportMatrix();
                float heightScale = 1;
                var screenToVisual = visualToScreen.Inverted();

                var center = new Vector4(g.Positions[0], 1);
                var screenPoint = Vector4.Transform(center, visualToScreen);
                var spw = screenPoint.W;
                var spx = screenPoint.X;
                var spy = screenPoint.Y;
                var spz = screenPoint.Z;
                var left = -g.Width / 2;
                var right = g.Width / 2;
                var top = -g.Height / 2 * heightScale;
                var bottom = g.Height / 2 * heightScale;
                //Debug.WriteLine(spw);
                // Debug.WriteLine(string.Format("Z={0}; W={1}", spz, spw));
                var bl = new Vector4(spx + left * spw, spy + bottom * spw, spz, spw);
                bl = Vector4.Transform(bl, screenToVisual);
                bl /= bl.W;

                var br = new Vector4(spx + right * spw, spy + bottom * spw, spz, spw);
                br = Vector4.Transform(br, screenToVisual);
                br /= br.W;

                var tr = new Vector4(spx + right * spw, spy + top * spw, spz, spw);
                tr = Vector4.Transform(tr, screenToVisual);
                tr /= tr.W;

                var tl = new Vector4(spx + left * spw, spy + top * spw, spz, spw);
                tl = Vector4.Transform(tl, screenToVisual);
                tl /= tl.W;

                var b = BoundingBox.FromPoints(new Vector3[] { tl.ToVector3(), tr.ToVector3(), bl.ToVector3(), br.ToVector3() });

                // this all happens now in world space now:
                //Debug.WriteLine(string.Format("RayPosition:{0}; Direction:{1};", rayWS.Position, rayWS.Direction));
                if (rayWS.Intersects(ref b))
                {

                    float distance;
                    if (Collision.RayIntersectsBox(ref rayWS, ref b, out distance))
                    {
                        h = true;
                        result.ModelHit = this;
                        result.IsValid = true;
                        result.PointHit = (rayWS.Position + (rayWS.Direction * distance)).ToPoint3D();
                        result.Distance = distance;
                        //Debug.WriteLine(string.Format("Hit; HitPoint:{0}; Bound={1}; Distance={2}", result.PointHit, b, distance));
                    }
                }
            }
            if (h)
            {
                hits.Add(result);
            }
            return h;
        }

        protected override RenderTechnique SetRenderTechnique(IRenderHost host)
        {
            return host.RenderTechniquesManager.RenderTechniques[DefaultRenderTechniqueNames.BillboardText];
        }

        protected override bool CheckGeometry()
        {
            return Geometry is IBillboardText;
        }
        protected override bool OnAttach(IRenderHost host)
        {
            // --- attach
            if (!base.OnAttach(host))
            {
                return false;
            }

            // --- get variables
            vertexLayout = renderHost.EffectsManager.GetLayout(renderTechnique);
            effectTechnique = effect.GetTechniqueByName(renderTechnique.Name);
            // --- transformations
            effectTransforms = new EffectTransformVariables(effect);

            // --- shader variables
            vViewport = effect.GetVariableByName("vViewport").AsVector();

            // --- get geometry
            var geometry = Geometry as IBillboardText;
            if (geometry == null)
            {
                throw new System.Exception("Geometry must implement IBillboardText");
            }
            // -- set geometry if given
            vertexBuffer = Device.CreateBuffer(BindFlags.VertexBuffer,
                VertexSizeInBytes, CreateBillboardVertexArray(), geometry.Positions.Count);
            // --- material 
            // this.AttachMaterial();
            this.bHasBillboardTexture = effect.GetVariableByName("bHasTexture").AsScalar();
            this.billboardTextureVariable = effect.GetVariableByName("billboardTexture").AsShaderResource();
            if (geometry.Texture != null)
            {
                var textureBytes = geometry.Texture.ToByteArray();
                billboardTextureView = TextureLoader.FromMemoryAsShaderResourceView(Device, textureBytes);
            }

            this.billboardAlphaTextureVariable = effect.GetVariableByName("billboardAlphaTexture").AsShaderResource();
            this.bHasBillboardAlphaTexture = effect.GetVariableByName("bHasAlphaTexture").AsScalar();
            if (geometry.AlphaTexture != null)
            {
                billboardAlphaTextureView = global::SharpDX.Toolkit.Graphics.Texture.Load(Device, geometry.AlphaTexture);
            }
            // --- set rasterstate
            OnRasterStateChanged();

            // --- flush
            //Device.ImmediateContext.Flush();
            return true;
        }

        protected override void OnDetach()
        {
            Disposer.RemoveAndDispose(ref vViewport);
            Disposer.RemoveAndDispose(ref billboardTextureVariable);
            Disposer.RemoveAndDispose(ref billboardTextureView);
            Disposer.RemoveAndDispose(ref billboardAlphaTextureVariable);
            Disposer.RemoveAndDispose(ref billboardAlphaTextureView);
            Disposer.RemoveAndDispose(ref bHasBillboardAlphaTexture);
            Disposer.RemoveAndDispose(ref bHasBillboardTexture);
            base.OnDetach();
        }

        protected override void OnRender(RenderContext renderContext)
        {
            // --- check to render the model
            var geometry = Geometry as IBillboardText;
            if (geometry == null)
            {
                throw new System.Exception("Geometry must implement IBillboardText");
            }

            // --- set constant paramerers             
            var worldMatrix = modelMatrix * renderContext.worldMatrix;
            effectTransforms.mWorld.SetMatrix(ref worldMatrix);

            // --- check shadowmaps
            //this.hasShadowMap = this.renderHost.IsShadowMapEnabled;
            //this.effectMaterial.bHasShadowMapVariable.Set(this.hasShadowMap);

            // --- set context
            renderContext.DeviceContext.InputAssembler.InputLayout = vertexLayout;
            renderContext.DeviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

            // --- set rasterstate            
            renderContext.DeviceContext.Rasterizer.State = rasterState;

            // --- bind buffer                
            renderContext.DeviceContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, BillboardVertex.SizeInBytes, 0));
            // --- render the geometry
            this.bHasBillboardTexture.Set(geometry.Texture != null);
            if (geometry.Texture != null)
            {
                billboardTextureVariable.SetResource(billboardTextureView);
            }

            this.bHasBillboardAlphaTexture.Set(geometry.AlphaTexture != null);
            if (geometry.AlphaTexture != null)
            {
                billboardAlphaTextureVariable.SetResource(billboardAlphaTextureView);
            }

            var vertexCount = Geometry.Positions.Count;
            switch (billboardType)
            {
                case BillboardType.MultipleText:
                    // Use foreground shader to draw text
                    effectTechnique.GetPassByIndex(0).Apply(renderContext.DeviceContext);

                    // --- draw text, foreground vertex is beginning from 0.
                    renderContext.DeviceContext.Draw(vertexCount, 0);
                    break;
                case BillboardType.SingleText:
                    if (vertexCount == 12)
                    {
                        var half = vertexCount / 2;
                        // Use background shader to draw background first
                        effectTechnique.GetPassByIndex(1).Apply(renderContext.DeviceContext);
                        // --- draw background, background vertex is beginning from middle. <see cref="BillboardSingleText3D"/>
                        renderContext.DeviceContext.Draw(half, half);

                        // Use foreground shader to draw text
                        effectTechnique.GetPassByIndex(0).Apply(renderContext.DeviceContext);

                        // --- draw text, foreground vertex is beginning from 0.
                        renderContext.DeviceContext.Draw(half, 0);
                    }
                    break;
                case BillboardType.SingleImage:
                    // Use foreground shader to draw text
                    effectTechnique.GetPassByIndex(2).Apply(renderContext.DeviceContext);

                    // --- draw text, foreground vertex is beginning from 0.
                    renderContext.DeviceContext.Draw(vertexCount, 0);
                    break;
            }
        }

        #endregion

        #region Private Helper Methdos

        private BillboardVertex[] CreateBillboardVertexArray()
        {
            var billboardGeometry = Geometry as IBillboardText;

            // Gather all of the textInfo offsets.
            // These should be equal in number to the positions.
            billboardType = billboardGeometry.Type;
            billboardGeometry.DrawTexture();

            var position = billboardGeometry.Positions.Array;
            var vertexCount = billboardGeometry.Positions.Count;
            if (!ReuseVertexArrayBuffer || vertexArrayBuffer == null || vertexArrayBuffer.Length < vertexCount)
                vertexArrayBuffer = new BillboardVertex[vertexCount];

            var allOffsets = billboardGeometry.TextureOffsets;

            for (var i = 0; i < vertexCount; i++)
            {
                var tc = billboardGeometry.TextureCoordinates[i];
                vertexArrayBuffer[i].Position = new Vector4(position[i], 1.0f);
                vertexArrayBuffer[i].Color = billboardGeometry.Colors[i];
                vertexArrayBuffer[i].TexCoord = new Vector4(tc.X, tc.Y, allOffsets[i].X, allOffsets[i].Y);
            }

            return vertexArrayBuffer;
        }

        #endregion
    }
}
