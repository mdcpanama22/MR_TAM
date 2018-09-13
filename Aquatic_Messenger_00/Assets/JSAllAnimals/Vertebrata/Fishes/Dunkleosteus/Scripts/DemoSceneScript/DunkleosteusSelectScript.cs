using UnityEngine;
using System.Collections;

public class DunkleosteusSelectScript : MonoBehaviour {

	public GameObject[] dunkleosteus;
	public DunkleosteusCameraScript dunkleCamera;
	public GameObject[] sliders;

	public void DunkleSelect(int dunkleNum){
		dunkleCamera.target=dunkleosteus[dunkleNum];
		foreach(GameObject aDunkle in dunkleosteus){
			aDunkle.GetComponent<DunkleosteusUserController>().enabled=false;
		}

		foreach(GameObject aSlider in sliders){
			aSlider.SetActive(false);
		}
		dunkleosteus[dunkleNum].GetComponent<DunkleosteusUserController>().enabled=true;	
		sliders [dunkleNum].SetActive(true);
	}

}
