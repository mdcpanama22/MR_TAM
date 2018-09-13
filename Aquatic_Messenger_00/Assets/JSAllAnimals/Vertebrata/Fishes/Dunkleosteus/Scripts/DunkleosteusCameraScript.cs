using UnityEngine;
using System.Collections;

public class DunkleosteusCameraScript : MonoBehaviour {

	public GameObject target;
	public float turnSpeed=.2f;


	void FixedUpdate(){
		transform.position = Vector3.Lerp (transform.position,target.transform.position,Time.deltaTime*5);
		transform.rotation = Quaternion.Lerp (transform.rotation,target.transform.rotation,Time.deltaTime*turnSpeed);
	}
}
