using UnityEngine;
using System.Collections;

public class DunkleosteusUserController : MonoBehaviour {
	DunkleosteusCharacter dunkleosteusCharacter;

	void Start () {
		dunkleosteusCharacter = GetComponent<DunkleosteusCharacter> ();
	}

	void Update () {
		if (Input.GetButtonDown ("Fire1")) {
			dunkleosteusCharacter.Attack();
		}

		dunkleosteusCharacter.turnSpeed=Input.GetAxis ("Horizontal");
		dunkleosteusCharacter.upDownSpeed= -Input.GetAxis ("Vertical");
	}
}
