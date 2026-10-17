using System;
using UnityEngine;

namespace Kingsfall
{
    [Serializable]
    public class ShardSet
    {
        public Mesh[] meshes;
        public Vector3[] offsets;
    }

    /// <summary>Everything the editor builder generates, loaded at runtime from Resources.</summary>
    public class KingsfallLibrary : ScriptableObject
    {
        [Header("Pieces (index = PieceType)")]
        public Mesh[] pieceMeshes = new Mesh[7];
        public ShardSet[] shards = new ShardSet[7];

        [Header("Materials")]
        public Material whitePiece;
        public Material blackPiece;
        public Material sageRobe;
        public Material marker;
        public Material captureMarker;
        public Material selectMarker;
        public Material lastMove;
        public Material fire;
        public Material ember;
        public Material dust;
        public Material spark;
        public Material lightning;
        public Material ring;
        public Material orb;
        public Material beam;
        public Material smoke;

        [Header("Meshes")]
        public Mesh markerDisc;
        public Mesh ringMesh;
        public Mesh sphere;
        public Mesh grandmasterMesh;
        public Mesh grandmasterSkinMesh;

        [Header("Fonts")]
        public Font titleFont;
        public Font headFont;
        public Font bodyFont;
        public Font symbolFont;

        static KingsfallLibrary instance;
        public static KingsfallLibrary I
        {
            get
            {
                if (instance == null) instance = Resources.Load<KingsfallLibrary>("KingsfallLibrary");
                return instance;
            }
        }
    }
}
