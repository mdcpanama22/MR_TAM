using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeepAnimatorOn : MonoBehaviour {
    public Animator theAnimator;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        gameObject.GetComponent<Animator>().enabled = true;
	}
}
