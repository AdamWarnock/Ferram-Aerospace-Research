﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace FerramAerospaceResearch.FARAeroComponents
{
    //Engines handle ram drag at full throttle, but as throttle drops so does ram drag
    //This attempts some manner of handling ram drag at various speeds
    class VesselIntakeRamDrag
    {
        const float AVG_NOZZLE_VEL_RELATIVE_TO_FREESTREAM = 0.25f;       //assume value approximately for turbojets
        const float AVG_NOZZLE_VEL_FACTOR = AVG_NOZZLE_VEL_RELATIVE_TO_FREESTREAM * (1 - AVG_NOZZLE_VEL_RELATIVE_TO_FREESTREAM);

        List<FARAeroPartModule> _aeroModulesWithIntakes = new List<FARAeroPartModule>();
        List<ModuleResourceIntake> _intakeModules = new List<ModuleResourceIntake>();
        List<Transform> _intakeTransforms = new List<Transform>();
        List<ModuleEngines> _airBreathingEngines = new List<ModuleEngines>();

        public void UpdateAeroData(List<FARAeroPartModule> allUsedAeroModules, List<FARAeroPartModule> allUnusedAeroModules)
        {
            _aeroModulesWithIntakes.Clear();
            _intakeModules.Clear();
            _intakeTransforms.Clear();
            _airBreathingEngines.Clear();

            for(int i = 0; i < allUsedAeroModules.Count; i++)       //get all exposed intakes and engines
            {
                FARAeroPartModule aeroModule = allUsedAeroModules[i];
                if (aeroModule == null)
                    continue;
                Part p = aeroModule.part;

                if(p.Modules.Contains("ModuleResourceIntake"))
                {
                    ModuleResourceIntake intake = (ModuleResourceIntake)p.Modules["ModuleResourceIntake"];
                    _aeroModulesWithIntakes.Add(aeroModule);
                    _intakeModules.Add(intake);
                    _intakeTransforms.Add(p.FindModelTransform(intake.intakeTransformName));
                }
                if(p.Modules.Contains("ModuleEngines"))
                {
                    ModuleEngines engines = (ModuleEngines)p.Modules["ModuleEngines"];
                    for (int j = 0; j < engines.propellants.Count; j++)
                    {
                        Propellant prop = engines.propellants[j];
                        if (prop.name == "IntakeAir")
                        {
                            _airBreathingEngines.Add(engines);
                            break;
                        }
                    }
                }
                if (p.Modules.Contains("ModuleEnginesFX"))
                {
                    ModuleEnginesFX engines = (ModuleEnginesFX)p.Modules["ModuleEnginesFX"];
                    for (int j = 0; j < engines.propellants.Count; j++)
                    {
                        Propellant prop = engines.propellants[j];
                        if (prop.name == "IntakeAir")
                        {
                            _airBreathingEngines.Add(engines);
                            break;
                        }
                    }
                }
            }

            for(int i = 0; i < allUnusedAeroModules.Count; i++)     //get all covered airbreathing Engines
            {
                FARAeroPartModule aeroModule = allUnusedAeroModules[i];
                if (aeroModule == null)
                    continue;
                Part p = aeroModule.part;
                if (p.Modules.Contains("ModuleEngines"))
                {
                    ModuleEngines engines = (ModuleEngines)p.Modules["ModuleEngines"];
                    for (int j = 0; j < engines.propellants.Count; j++)
                    {
                        Propellant prop = engines.propellants[j];
                        if (prop.name == "IntakeAir")
                        {
                            _airBreathingEngines.Add(engines);
                            break;
                        }
                    }
                }
                if (p.Modules.Contains("ModuleEnginesFX"))
                {
                    ModuleEnginesFX engines = (ModuleEnginesFX)p.Modules["ModuleEnginesFX"];
                    for (int j = 0; j < engines.propellants.Count; j++)
                    {
                        Propellant prop = engines.propellants[j];
                        if (prop.name == "IntakeAir")
                        {
                            _airBreathingEngines.Add(engines);
                            break;
                        }
                    }
                }
            }
        }

        public void ApplyIntakeRamDrag(float machNumber, Vector3 vesselVelNorm, float dynPres)
        {
            float currentRamDrag = CalculateRamDrag(machNumber);
            ApplyIntakeDrag(currentRamDrag, vesselVelNorm, dynPres);
        }

        private float CalculateRamDrag(float machNumber)
        {
            float currentThrottle = 0;

            for (int i = 0; i < _airBreathingEngines.Count; i++)
            {
                currentThrottle += _airBreathingEngines[i].currentThrottle;
            }
            currentThrottle /= (float)_airBreathingEngines.Count;

            float currentRamDrag = RamDragPerArea(machNumber);
            currentRamDrag *= 1f - currentThrottle;

            return currentRamDrag;
        }

        private void ApplyIntakeDrag(float currentRamDrag, Vector3 vesselVelNorm, float dynPres)
        {
            for(int i = 0; i < _intakeTransforms.Count; i++)
            {
                ModuleResourceIntake intake = _intakeModules[i];
                if (!intake.intakeEnabled)
                    continue;


                float cosAoA = Vector3.Dot(_intakeTransforms[i].forward, vesselVelNorm);
                if (cosAoA < 0)
                    cosAoA = 0;

                if (cosAoA <= intake.aoaThreshold)
                    continue;

                FARAeroPartModule aeroModule = _aeroModulesWithIntakes[i];

                aeroModule.AddLocalForce(-aeroModule.partLocalVelNorm * dynPres * cosAoA * currentRamDrag * intake.area * 100, Vector3.zero);
            }
        }

        private float RamDragPerArea(float machNumber)
        {
            float drag = machNumber * machNumber;
            ++drag;
            drag = 2f / drag;
            drag *= AVG_NOZZLE_VEL_FACTOR;  //drag based on the nozzle

            drag += 0.1f;           //drag based on inlet
                                    //assuming inlet and nozzle area are equal
            
            return drag;
        }
    }
}