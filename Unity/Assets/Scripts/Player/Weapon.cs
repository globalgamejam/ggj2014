﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;

public class Weapon : MonoBehaviour
{

    public Transform weaponTransform;

    public float minElevation = -70;
    public float maxElevation = 70;

    public float elevationSpeed = 180;

    private float _elevation;
    private float _targetElevation;

    public GameObject muzzleFlash;
    public GameObject particles;
    private GameSystem _gameSystem;

    public float gunRange = 20;
    public float gunCone = 20;

    private bool _isLoaded = true;

    [System.Serializable]
    public class ConePoints
    {
        public float distance;
        public float diameter;

        public ConePoints(float distance = 0, float diameter = 2)
        {
            this.distance = distance;
            this.diameter = diameter;
        }
    }

    //sounds
    public AudioSource weaponSource;
    public AudioClip shotgunBlast;
    public AudioClip reloadEffect;


    public ConePoints[] cone = new ConePoints[]{new ConePoints(0,0), new ConePoints(10, 2), new ConePoints(100,2), };
    public float reloadTime = 2.4f;
    public float defaultReloadTime = 2.4f;
    private Animator _animator;

    private bool _isJuggernaut;
    public GameObject juggernautParticle;

    public bool ShotReady
    {
        get { return _isLoaded; }
    }

    protected void Awake()
    {
        _gameSystem = FindObjectOfType<GameSystem>();
        _animator = GetComponentInChildren<Animator>();

    }


    protected void Start()
    {
        var color = GetComponent<Player>().col;

        if(_isJuggernaut)
            color = new Color(255/255.0f, 61/255.0f, 104/255.0f, 1);
        weaponTransform.GetComponentInChildren<Renderer>().materials[2].SetColor("_MainColor", color);

        if(!_isJuggernaut)
            juggernautParticle.GetComponent<ParticleSystem>().Stop();
    }

    public void SetJuggernaut()
    {
        juggernautParticle.GetComponent<ParticleSystem>().Play();
        juggernautParticle.light.enabled = true;
        _isJuggernaut = true;
        var color = new Color(237/255.0f, 0, 52/255.0f, 1);
        _animator.SetFloat("Juggernaut", 1);
        reloadTime = defaultReloadTime*0.2667f;
        weaponTransform.GetComponentInChildren<Renderer>().materials[2].SetColor("_MainColor", color);
    }

    public void ResetFromJuggernaut()
    {
        _isJuggernaut = false;
        juggernautParticle.GetComponent<ParticleSystem>().Stop();
        juggernautParticle.light.enabled = false;
        var color = GetComponent<Player>().col;
        reloadTime = defaultReloadTime;
        weaponTransform.GetComponentInChildren<Renderer>().materials[2].SetColor("_MainColor", color);
        _animator.SetFloat("Juggernaut", 0);
    }


    public void ElevationInput(float angle)
    {
        _elevation += Mathf.Clamp(angle, -elevationSpeed, elevationSpeed);
    }

    private float _lastFrameMovement;
    protected void Update()
    {

        _elevation = Mathf.Clamp(_elevation, minElevation, maxElevation);
        weaponTransform.localRotation = Quaternion.Euler(_elevation, 0, 0);

        var velocity = rigidbody.velocity;
        velocity.y = 0;
        var clamp01 = Mathf.Clamp01(velocity.magnitude/5);
        _lastFrameMovement = _lastFrameMovement*0.90f + clamp01*.1f;
        _animator.SetFloat("MoveSpeed", _lastFrameMovement);
    }

    public void Shoot()
    {
        if (!_isLoaded)
            return;

        _isLoaded = false;

        float maxDist =float.MaxValue;
        RaycastHit hitInfo;
        if (Physics.Raycast(weaponTransform.position, weaponTransform.forward, out hitInfo))
            maxDist = hitInfo.distance;

        foreach (var player in _gameSystem.players)
        {
            float dist;
            if(player == gameObject || !player.activeSelf || !TestPlayerHit(player, out dist))
                continue;

            if (dist > maxDist)
                maxDist = dist;
            
            player.SendMessage("GotHit", this);
            
        }

        _animator.SetTrigger("Shoot");


        if (muzzleFlash != null)
        {
            var flash = (GameObject)Instantiate(muzzleFlash, weaponTransform.position + weaponTransform.forward - weaponTransform.up * 0.2f + weaponTransform.right * 0.2f, weaponTransform.rotation * Quaternion.Euler(0, 180, 0));
            flash.transform.parent = weaponTransform;
            Destroy(flash, 0.2f);
        }
        if (particles != null)
        {
            var part = Instantiate(particles, weaponTransform.position + weaponTransform.forward, weaponTransform.rotation);
            Destroy(part, Mathf.Min(1f, maxDist / 50f));
        }

        
        //play sound
        weaponSource.clip = shotgunBlast;
        weaponSource.Play();

        if (GameSystem.Instance.CurrentGameMode != GameSystem.GameMode.OneShot)
            StartCoroutine(Reload());

        AudioManager.Instance.ShotsFired();
    }

    public void StartReload()
    {
        if (!gameObject.activeSelf)
            _isLoaded = true;
        else
            StartCoroutine(Reload());
    }

    public void InstantReload()
    {
        _isLoaded = true;
        _isReloading = false;
    }

    private bool _isReloading;
    protected IEnumerator Reload()
    {
        if (_isReloading)
            yield break;
        _animator.SetTrigger("Reload");

        _isReloading = true;
        const float shootAnimTime = 0.1f;
        
            
        if(_isJuggernaut)
            yield return new WaitForSeconds(0.03f);
        else
            yield return new WaitForSeconds(shootAnimTime);
        weaponSource.PlayOneShot(reloadEffect);
        yield return new WaitForSeconds(reloadTime - shootAnimTime);
        _isLoaded = true;
        _isReloading = false;
    }

    private bool TestPlayerHit(GameObject player, out float dist)
    {
        dist = 0;

        var origin = weaponTransform.position;
        var direction = weaponTransform.forward;

        float distance = Vector3.Dot(direction, player.transform.position - origin);
        var closestPoint = player.collider.ClosestPointOnBounds(origin + distance*direction);

        var dispFromCenter = closestPoint - origin;
        dispFromCenter -= direction*Vector3.Dot(dispFromCenter, direction);

        int coneIndex = 0;
        while (coneIndex < cone.Length && cone[coneIndex].distance < distance)
        {
            coneIndex++;
        }

        if (coneIndex >= cone.Length)
            return false; //target is out of range

        if (coneIndex <= 0)
            return false; //target is less than minumum range (probably behind shooter)

        if((closestPoint - origin).magnitude > 0.1f
            && Physics.Raycast(origin, closestPoint - origin, (closestPoint - origin).magnitude  - 0.1f, ~(1 << 8))
            && (player.transform.position - origin).magnitude > 0.1f
            && Physics.Raycast(origin, player.transform.position - origin, (player.transform.position - origin).magnitude - 0.1f, ~(1 << 8)))
                    return false;

        dist = distance;
        float segProg = (distance - cone[coneIndex-1].distance) / (cone[coneIndex].distance - cone[coneIndex-1].distance);

        return dispFromCenter.magnitude < Mathf.Lerp(cone[coneIndex-1].diameter, cone[coneIndex].diameter, segProg) / 2;
    }
}
