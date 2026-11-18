import { useEffect, useState } from 'react';
import { api, unwrap } from '../api/client';

export interface Option {
  label: string;
  value: string;
}

export interface MasterOptions {
  units: Option[];
  categories: Option[];
  warehouses: Option[];
  locations: Option[];
  materials: Option[];
  suppliers: Option[];
  customers: Option[];
}

const empty: MasterOptions = {
  units: [],
  categories: [],
  warehouses: [],
  locations: [],
  materials: [],
  suppliers: [],
  customers: [],
};

export function useMasterOptions(): MasterOptions {
  const [options, setOptions] = useState<MasterOptions>(empty);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      const [units, categories, warehouses, locations, materials, suppliers, customers] = await Promise.allSettled([
        api.get('/api/unit-of-measure/all'),
        api.get('/api/material-category/all'),
        api.get('/api/warehouse/all'),
        api.get('/api/storage-location/all'),
        api.get('/api/material/all'),
        api.get('/api/supplier/all'),
        api.get('/api/customer/all'),
      ]);
      if (cancelled) return;
      const toOptions = (result: PromiseSettledResult<{ data: unknown }>, labelKey: string) => {
        if (result.status !== 'fulfilled') return [];
        const payload = unwrap<unknown[]>(result.value.data);
        return Array.isArray(payload)
          ? (payload as Record<string, unknown>[]).map((item) => ({
              label: `${item.code} - ${item[labelKey]}`,
              value: String(item.id),
            }))
          : [];
      };
      setOptions({
        units: toOptions(units, 'name'),
        categories: toOptions(categories, 'name'),
        warehouses: toOptions(warehouses, 'name'),
        locations: toOptions(locations, 'name'),
        materials: toOptions(materials, 'name'),
        suppliers: toOptions(suppliers, 'name'),
        customers: toOptions(customers, 'name'),
      });
    }
    load();
    return () => {
      cancelled = true;
    };
  }, []);

  return options;
}