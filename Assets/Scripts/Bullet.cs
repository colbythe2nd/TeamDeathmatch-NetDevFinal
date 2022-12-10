using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    public NetworkVariable<int> Damage = new NetworkVariable<int>(1);
    public NetworkVariable<Color> PlayerColor = new NetworkVariable<Color>(Color.black);
}
