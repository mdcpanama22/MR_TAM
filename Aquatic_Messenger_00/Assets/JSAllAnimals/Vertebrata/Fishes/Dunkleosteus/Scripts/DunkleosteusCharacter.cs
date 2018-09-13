using UnityEngine;
using System.Collections;

public class DunkleosteusCharacter : MonoBehaviour {
	public Animator dunkleosteusAnimator;
	Rigidbody dunkleosteusRigid;
	public float maxTurnSpeed=5000f;
	public float maxForwardSpeed=30000f;
	public float turnSpeed;
	public float upDownSpeed;
	public float forwardSpeed;

	void Start () {
		dunkleosteusAnimator = GetComponent<Animator> ();
		dunkleosteusRigid = GetComponent<Rigidbody> ();
	}

	void Update(){
		Move ();
	}

	public void Move(){
		dunkleosteusAnimator.SetFloat ("UpDown", upDownSpeed);
		dunkleosteusAnimator.SetFloat ("Turn", turnSpeed);
		dunkleosteusAnimator.SetFloat ("Forward", forwardSpeed);

		dunkleosteusRigid.AddTorque (transform.up*maxTurnSpeed*turnSpeed);
		dunkleosteusRigid.AddTorque (transform.right*maxTurnSpeed*(-upDownSpeed));
		dunkleosteusRigid.AddForce (transform.forward*maxForwardSpeed*forwardSpeed);
	}

	public void SpeedChange(float speed){
		forwardSpeed = speed;
	}

	public void Attack(){
		dunkleosteusAnimator.SetTrigger ("Attack");
	}
}
