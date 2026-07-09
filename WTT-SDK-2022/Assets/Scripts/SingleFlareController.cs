using System;
using MultiFlare;
using UnityEngine;
using UnityEngine.Serialization;

// Token: 0x020008B9 RID: 2233
[ExecuteInEditMode]
public class SingleFlareController : MonoBehaviour
{
	// Token: 0x0600489F RID: 18591 RVA: 0x0039AAB8 File Offset: 0x00398CB8
	public void OnEnable()
	{
	}

	// Token: 0x060048A0 RID: 18592 RVA: 0x001A826E File Offset: 0x001A646E
	public void OnDisable()
	{
	}

	// Token: 0x060048A1 RID: 18593 RVA: 0x0039AB0C File Offset: 0x00398D0C
	public void Update()
	{
		this.method_0();
	}

	// Token: 0x060048A2 RID: 18594 RVA: 0x001A828E File Offset: 0x001A648E
	public static float smethod_0(float value, float inMin, float inMax)
	{
		return (value - inMin) / (inMax - inMin);
	}

	// Token: 0x060048A3 RID: 18595 RVA: 0x001A8297 File Offset: 0x001A6497
	public void method_0()
	{
	}

	// Token: 0x04003E1A RID: 15898
	[SerializeField]
	private float _changeSpeed = 2f;

	// Token: 0x04003E1B RID: 15899
	[SerializeField]
	[Range(0f, 1f)]
	private float _minAngle = 0.6f;

	// Token: 0x04003E1C RID: 15900
	[SerializeField]
	[FormerlySerializedAs("Light")]
	private FlareLight _light;
}
