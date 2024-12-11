using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Profiler;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{
		//Information----------------------------
		//script for controlling a rotary missile magazine/printer
		public string magTag = "[RotoMag]";			//group identifier for a magazine
		//---------------------------------------------
		//Dictionary for storing mags
		public Dictionary<string, magazine> magazines = new Dictionary<string, magazine>();
		//---------------------------------------------
		//funtional varibles used to store printer and system status infortmation
		public bool armed = false; //player input controlled
		public Dictionary<string, string> state = new Dictionary<string, string>(); // determinstic status of each printer
		Dictionary<string, int> thinge = new Dictionary<string, int>();
		Dictionary<string, int> tfuel = new Dictionary<string, int>();
		Dictionary<string, int> tfired = new Dictionary<string, int>();
		//--------------------------------------------
		//set to accept command inputs
		MyCommandLine _commandLine = new MyCommandLine();
		Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
		//class to define our magazine objects, I hope the attribute names are self explanatory...
		public class magazine //class to define and interact with a magazine and its components
		{
			public IMyMotorStator rotor;
			public IMyTerminalBlock projector;
			public IMyGyro gyro;
			public List<IMyShipMergeBlock> mergeBlocks = new List<IMyShipMergeBlock>();
			public List<IMyTerminalBlock> connectors = new List<IMyTerminalBlock>();
			public List<IMyTerminalBlock> welders = new List<IMyTerminalBlock>();
			public List<IMyTerminalBlock> doors = new List<IMyTerminalBlock>();
			public string status = "idle";
			public double position;
			public double comandedPosition = 1;
			public int slots;
			public int stepAngle;
			public string name;
		}

		public void findMags(Dictionary<string, magazine> maglist) //discover magazines to control
		{

			//get list of all groups on grid
			var Groups = new List<IMyBlockGroup>();
			GridTerminalSystem.GetBlockGroups(Groups);
			//search that list for our torpedo printers
			Echo("Looking for magazines");
			foreach (var group in Groups)
			{
				if (maglist.Keys.Contains(group.Name)) 
				{
					Echo($"Magazine {group.Name} already exists, skipping...");
					continue; 
				}
				else if (group.Name.Contains(magTag))
				{
					string magname = group.Name;
					Echo($"found group {magname}");
					magazines[magname] = new magazine();
					magazines[magname].name = magname;
					List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
					group.GetBlocks(blocks);
					foreach (var block in blocks)
					{
						string type = block.GetType().Name;
						//Echo(type);
						if (type == "MyMotorAdvancedStator")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].rotor = block as IMyMotorStator;
						}
						else if (type == "MyShipMergeBlock")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].mergeBlocks.Add(block as IMyShipMergeBlock);

						}
						else if (type == "MyShipConnector")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].connectors.Add(block);

						}
						else if (type == "MyShipWelder")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].welders.Add(block);

						}
						else if (type == "MyAirtightSlideDoor")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].doors.Add(block);

						}
						else if (type == "MySpaceProjector")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].projector = block;

						}
						else if (type == "MyGyro")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].gyro = block as IMyGyro;

						}
						//else Echo($"ignoring block \"{block.CustomName}\" of unknown type \"{block.GetType().Name}\"");
					}

				}
			}
			foreach (var mag in magazines)
			{
				var val = mag.Value;
				var key = mag.Key;
				if (
					(val.doors.Count == 0) |
					(val.welders.Count == 0) |
					(val.mergeBlocks.Count == 0) |
					(val.connectors.Count == 0) |
					(val.rotor == null) |
					(val.projector == null) |
					(val.gyro == null)
					)
				{
					Echo($"\"{key}\" is missing parts!");
				}
				else Echo($"\"{key}\" is complete.");
				Echo($"found {val.doors.Count} doors.");
				Echo($"found {val.welders.Count} welders.");
				Echo($"found {val.mergeBlocks.Count} merge blocks.");
				Echo($"found {val.connectors.Count} connectors.");
				Echo($"found rotor \"{val.rotor.CustomName}\"");
				Echo($"found projector \"{val.projector.CustomName}\"");
				Echo($"found gyro \"{val.gyro.CustomName}\"");
			}

		}

		public void bootMags() //perform initialization of mags - set their parameters and rotate them to visually confirm they work
		{
			foreach (var entry in magazines)
			{
				magazine mag = entry.Value;
				string name = entry.Key;
				magazines[name].slots = magazines[name].mergeBlocks.Count;
				magazines[name].stepAngle = 360 / magazines[name].slots;
				magazines[name].position = (mag.rotor.Angle / 2 * Math.PI * magazines[name].slots +1);
				mag.status = "Bootup Sequence";
				Single ll = -361;
				Single ul = 361;
				Single v = 2;
				mag.rotor.SetValue("LowerLimit", ll);
				mag.rotor.SetValue("UpperLimit", ul);
				mag.rotor.SetValue("Velocity", v);
			}
			// allow 100 ticks for boot cycle to physically move discovered rotors - this is to allow players to visually validate the assembly works
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public void sitrep() //provide a status report of all known magazines to progblock console and text panel
		{
			string report = "";
			var block = Me;
			var surface = block is IMyTextSurface
				? (IMyTextSurface)block
				: ((IMyTextSurfaceProvider)block).GetSurface(0);
			surface.ContentType = ContentType.TEXT_AND_IMAGE;
			surface.WriteText("");
			foreach (var entry in magazines) { 
				string name = entry.Key;
				var mag = entry.Value;
				report += $"Magazine {name}\n";
				report += $"State: {mag.status}\n";
				report += $"Position: {mag.position}\n";
				foreach (var merge in magazines[name].mergeBlocks)
				{
					report += $"{merge.CustomName} Connected: {merge.IsConnected}\n";
				}
				report += "\n";
			}
			surface.WriteText(report);
			Echo(report);
		}

		public void monitor() //monitor the status of the magazine to see if it matches the commanded status and to evalute if actions need to be performed
		{ 
		foreach (var mag in magazines.Values)
			{
				magazines[mag.name].position = (mag.rotor.Angle / (2 * Math.PI) * magazines[mag.name].slots + 1);
				mag.position = Math.Round(magazines[mag.name].position, 2);
				mag.comandedPosition = Math.Round(magazines[mag.name].comandedPosition, 2);
				if (mag.comandedPosition != mag.position)
				{
					rotateMag(mag.name, mag.comandedPosition);
				}
				else if (mag.position == mag.comandedPosition)
				{
					magazines[mag.name].rotor.TargetVelocityRPM = 0;
					mag.rotor.UpperLimitRad = mag.rotor.Angle;
					mag.rotor.LowerLimitRad = mag.rotor.Angle;
					magazines[mag.name].status = $"Idle in position {mag.position}";
					mag.gyro.GyroOverride = false;
				}

			}
		}

		public void parsecommands(string argument) //handle user input in the run field of the progblock
		{
			if (_commandLine.TryParse(argument))
			{
				Action commandAction;

				// Retrieve the first argument. Switches are ignored.
				string command = _commandLine.Argument(0);

				// Now we must validate that the first argument is actually specified, 
				// then attempt to find the matching command delegate.
				if (command == null)
				{
					Echo("No command specified");
				}
				else if (_commands.TryGetValue(command, out commandAction))
				{
					// We have found a command. Invoke it.
					commandAction();
				}
				else
				{
					Echo($"Unknown command {command}");
				}
			}

		}

		public void step() //increment the commanded position of all magazines by 1, this will cause the mags to advance on next program loop  this is mainly a troubleshooting measure
		{
			foreach (var mag in magazines.Values) {
				magazines[mag.name].comandedPosition += 1;
				if (magazines[mag.name].comandedPosition > (mag.slots))
				{
					magazines[mag.name].comandedPosition = magazines[mag.name].comandedPosition - magazines[mag.name].slots;
				}
			}
		
		}

		public void rotateMag(string magname, double newpos) //monitor function will call this if it discovers a misalignment of greater than 1% of 1 step.  this function can also be used directly to command a new mag position
		{
			var mag = magazines[magname];
			mag.gyro.GyroOverride = true; //set our gyro to hold current heading to counter torque of rotating the magazine
			magazines[magname].comandedPosition = newpos; //if we called this manually, we need to store the new commanded position so the monitor function does not move the mag back
			float gotoangle = (float)(mag.stepAngle * (mag.comandedPosition - 1));
			if (mag.position == mag.slots && mag.comandedPosition == 1) //check if we are at the last position.  if so, move FORWARD not BACKWARD
			{
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				mag.rotor.TargetVelocityRPM = 15;
				mag.rotor.UpperLimitDeg = gotoangle;
			}
			else if (mag.position > mag.comandedPosition)
			{
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				
				mag.rotor.TargetVelocityRPM = -15;
				mag.rotor.LowerLimitDeg = gotoangle;
			}
			else if (mag.position < mag.comandedPosition)
			{
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				mag.rotor.TargetVelocityRPM = 15;
				mag.rotor.UpperLimitDeg = gotoangle;
			}
			else if (mag.position == mag.comandedPosition)
			{
				magazines[mag.name].rotor.TargetVelocityRPM = 0;
				mag.rotor.UpperLimitDeg = gotoangle;
				mag.rotor.LowerLimitDeg = gotoangle;
				magazines[mag.name].status = $"Idle in position {mag.position}";
				mag.gyro.GyroOverride = false;
			}

		}

		//Startup sequence
		public Program()
		{

			findMags(magazines);
			bootMags();
			_commands["step"] = step;
		}




		public void Save()
		{
			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means. 
			// 
			// This method is optional and can be removed if not
			// needed.
		}

		public void Main(string argument, UpdateType updateSource)
		{
			// Configure this program to run the Main method every 10 update ticks
			//Runtime.UpdateFrequency = UpdateFrequency.Update10;
			monitor();
			sitrep();
			parsecommands(argument);
		}
	}
}
