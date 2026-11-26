export function sourceTargetDiscovery() {
  const folders = [
    'ProductionLineControl', 'IntegratedMaterialHandlingAndTracking', 'StationControllersForWorkpiecePreparation',
    'ConveyorAndWorkpieceTransferCoordination', 'InfeedAndOutfeedSynchronization', 'MachineSpecificWorkpieceProcessing',
    'AutomaticOperationAndInterlockManagement', 'CalculationOfConveyorPositionAndOffsets',
    'CommissioningAndProductionRecipeSpecificAdjustments',
  ];
  const targets = Array.from({ length: 500 }, (_, index) => {
    const owner = `FB_WorkpieceProcessing${index}`;
    const member = 'CalculateConveyorPosition';
    const qualifiedName = `${owner}.${member}`;
    const treePath = ['TIPC', 'LineControl', 'LineControl Project', 'POUs', ...folders, owner, member].join('^');
    const physicalFile = ['C:', 'TwinCAT', 'Projects', 'MachineController', 'LineControl', 'POUs', ...folders, owner + '.TcPOU'].join('\\');
    return {
      objectId: `${physicalFile}|${treePath}`, qualifiedName, kind: 'method', sections: ['declaration', 'implementation'],
      language: 'ST', treePath, physicalFile, source: 'automationInterface', sourceMode: 'automationInterface',
      persistence: 'mayPersistToDisk', applySupported: true,
    };
  });
  return { moniker: null, targets, count: targets.length, truncated: false, source: 'automationInterface', persistence: 'mayPersistToDisk' };
}
