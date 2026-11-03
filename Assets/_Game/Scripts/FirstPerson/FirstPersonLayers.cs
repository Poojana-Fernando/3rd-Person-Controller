using UnityEngine;

namespace Game.FirstPerson
{
    public static class FirstPersonLayers
    {
        public static int ViewmodelLayer => Resolve(FirstPersonConstants.ViewmodelLayerName, FirstPersonConstants.DefaultViewmodelLayer);
        public static int HiddenBodyLayer => Resolve(FirstPersonConstants.HiddenBodyLayerName, FirstPersonConstants.DefaultHiddenBodyLayer);

        static int Resolve(string name, int fallback)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? layer : fallback;
        }
    }
}
